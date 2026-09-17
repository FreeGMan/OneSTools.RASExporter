using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Transport;

public sealed class RasConnection : IAsyncDisposable
{
    private readonly EndPoint _remoteEndPoint;
    private readonly ILogger<RasConnection> _logger;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private PipeReader? _reader;
    private PipeWriter? _writer;
    
    private readonly CancellationTokenSource _connectionCts = new();
    private Task? _readLoopTask;
    private Task? _keepAliveTask;

    private readonly Channel<SwpFrame> _incomingFrames = Channel.CreateUnbounded<SwpFrame>();

    public TimeSpan KeepAliveTimeout { get; private set; } = TimeSpan.FromSeconds(20);
    
    public RasConnection(EndPoint remoteEndPoint, ILogger<RasConnection>? logger = null)
    {
        _remoteEndPoint = remoteEndPoint;
        _logger = logger ?? NullLogger<RasConnection>.Instance;
    }

    public async Task ConnectAsync(Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to {EndPoint}...", _remoteEndPoint);
        
        _tcpClient = new TcpClient();
        if (_remoteEndPoint is IPEndPoint ipEndPoint)
        {
            await _tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port, cancellationToken);
        }
        else
        {
            await _tcpClient.ConnectAsync((IPEndPoint)_remoteEndPoint, cancellationToken);
        }

        _networkStream = _tcpClient.GetStream();
        _reader = PipeReader.Create(_networkStream, new StreamPipeReaderOptions(leaveOpen: true));
        _writer = PipeWriter.Create(_networkStream, new StreamPipeWriterOptions(leaveOpen: true));

        // 1. Negotiate
        await SendNegotiateAsync(cancellationToken);

        // 2. Connect
        var connectParams = parameters ?? new Dictionary<string, object?>();
        if (!connectParams.ContainsKey("connect.timeout"))
            connectParams["connect.timeout"] = 2000;
            
        await SendConnectAsync(connectParams, cancellationToken);

        // 3. Wait for ConnectAck
        var ackFrame = await ReadSingleFrameAsync(cancellationToken);
        if (ackFrame.Type != (byte)SwpMessageType.ConnectAck)
        {
            throw new InvalidOperationException($"Expected ConnectAck (2), got type {ackFrame.Type}");
        }

        var ackParams = ParseParameters(ackFrame.Payload);
        if (ackParams.TryGetValue("nipple.swp.connection.keep_alive.timeout", out var kaTimeoutObj) && kaTimeoutObj is string kaTimeoutStr && int.TryParse(kaTimeoutStr, out var kaTimeoutSecs))
        {
            KeepAliveTimeout = TimeSpan.FromSeconds(kaTimeoutSecs);
        }

        _logger.LogInformation("Connected successfully. Keep-alive timeout: {Timeout}", KeepAliveTimeout);

        // Start background loops
        _readLoopTask = Task.Run(() => ReadLoopAsync(_connectionCts.Token));
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_connectionCts.Token));
    }

    private async Task SendNegotiateAsync(CancellationToken cancellationToken)
    {
        var buffer = _writer!.GetMemory(8);
        var bufferWriter = new ArrayBufferWriter<byte>(8);
        var writer = new RasWriter(bufferWriter);
        
        writer.WriteInt32(SwpProtocolConstants.Magic);
        writer.WriteShort(SwpProtocolConstants.ProtocolVersion);
        writer.WriteShort(SwpProtocolConstants.CodecVersion);

        bufferWriter.WrittenMemory.CopyTo(buffer);
        _writer.Advance(8);
        await _writer.FlushAsync(cancellationToken);
    }

    private async Task SendConnectAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(bufferWriter);
        RasParametersSerializer.WriteParameters(ref writer, parameters);

        await WriteFrameAsync((byte)SwpMessageType.Connect, bufferWriter.WrittenMemory, cancellationToken);
    }

    public async Task WriteFrameAsync(byte type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("Not connected");

        var headerWriter = new ArrayBufferWriter<byte>(10);
        var writer = new RasWriter(headerWriter);
        writer.WriteByte(type);
        writer.WriteSize(payload.Length);

        var headerMem = headerWriter.WrittenMemory;
        
        var memory = _writer.GetMemory(headerMem.Length + payload.Length);
        headerMem.CopyTo(memory);
        payload.CopyTo(memory.Slice(headerMem.Length));
        _writer.Advance(headerMem.Length + payload.Length);
        
        var result = await _writer.FlushAsync(cancellationToken);
        if (result.IsCompleted || result.IsCanceled)
        {
            throw new IOException("Connection closed during write");
        }
    }

    public ValueTask<SwpFrame> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        return _incomingFrames.Reader.ReadAsync(cancellationToken);
    }

    private async Task<SwpFrame> ReadSingleFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _reader!.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            if (TryReadFrame(ref buffer, out var frame))
            {
                _reader.AdvanceTo(buffer.Start, buffer.End);
                return frame;
            }

            _reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                throw new EndOfStreamException("Connection closed by remote host");
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _reader!.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TryReadFrame(ref buffer, out var frame))
                {
                    if (frame.Type == (byte)SwpMessageType.KeepAlive)
                    {
                        // Ignore KeepAlive
                        continue;
                    }
                    
                    await _incomingFrames.Writer.WriteAsync(frame, cancellationToken);
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read loop error");
        }
        finally
        {
            _incomingFrames.Writer.TryComplete();
            await DisposeAsync();
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        if (KeepAliveTimeout <= TimeSpan.Zero) return;

        try
        {
            // Ping every KeepAliveTimeout / 2 to be safe
            var pingInterval = TimeSpan.FromSeconds(KeepAliveTimeout.TotalSeconds / 2);
            using var timer = new PeriodicTimer(pingInterval);
            
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await WriteFrameAsync((byte)SwpMessageType.KeepAlive, ReadOnlyMemory<byte>.Empty, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keep-alive loop error");
        }
    }

    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out SwpFrame frame)
    {
        frame = default;
        if (buffer.IsEmpty) return false;

        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryRead(out byte type)) return false;

        // Try read size (LEB128)
        int size = 0;
        int shift = 0;
        bool sizeRead = false;

        while (reader.TryRead(out byte b))
        {
            size |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                sizeRead = true;
                break;
            }
            shift += 7;
            if (shift > 28) throw new InvalidDataException("Invalid varint");
        }

        if (!sizeRead) return false;

        if (reader.Remaining < size) return false;

        var payload = reader.UnreadSequence.Slice(0, size).ToArray();
        reader.Advance(size);
        
        frame = new SwpFrame(type, payload);
        buffer = buffer.Slice(reader.Position);
        return true;
    }

    private static Dictionary<string, object?> ParseParameters(byte[] payload)
    {
        if (payload.Length == 0) return new Dictionary<string, object?>();
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);
        return RasParametersSerializer.ReadParameters(ref reader);
    }

    public async ValueTask DisposeAsync()
    {
        _connectionCts.Cancel();

        // Close the underlying stream and socket immediately to break any blocking ReadAsync calls.
        _reader?.Complete();
        _writer?.Complete();
        _networkStream?.Dispose();
        _tcpClient?.Dispose();
        
        if (_readLoopTask is not null)
        {
            try { await Task.WhenAny(_readLoopTask, Task.Delay(500)); } catch { }
        }
        if (_keepAliveTask is not null)
        {
            try { await Task.WhenAny(_keepAliveTask, Task.Delay(500)); } catch { }
        }

        _connectionCts.Dispose();
    }
}

public readonly record struct SwpFrame(byte Type, byte[] Payload);

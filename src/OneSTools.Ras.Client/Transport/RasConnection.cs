using System;
using System.Buffers;
using System.Collections.Concurrent;
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
using OneSTools.Ras.Client.Exceptions;
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

    private readonly ConcurrentDictionary<ServiceId, TaskCompletionSource<RasEndpoint>> _pendingOpenEndpoints = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pendingRequests = new();
    private readonly ConcurrentDictionary<int, RasEndpoint> _activeEndpoints = new();

    private readonly Channel<SwpFrame> _unhandledFrames = Channel.CreateUnbounded<SwpFrame>();

    // PipeWriter не потокобезопасен: все записи фреймов (эндпоинты, keep-alive, close)
    // сериализуются этим гейтом.
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public TimeSpan KeepAliveTimeout { get; private set; } = TimeSpan.FromSeconds(20);

    /// <summary>Параметры, полученные в ConnectAck (server.name, keep_alive.timeout и т.п.), до установки соединения — null.</summary>
    public IReadOnlyDictionary<string, object?>? ConnectAckParameters { get; private set; }

    private int _closed;

    /// <summary>Соединение закрыто/смертельно (сокет умер, Dispose/Abort вызван). Повторно использовать нельзя.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    
    
    public RasConnection(EndPoint remoteEndPoint, ILogger<RasConnection>? logger = null)
    {
        _remoteEndPoint = remoteEndPoint;
        _logger = logger ?? NullLogger<RasConnection>.Instance;
    }

    public async Task ConnectAsync(Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        if (_readLoopTask is not null)
            throw new InvalidOperationException("RasConnection уже подключён; повторное подключение или использование после закрытия не поддерживается.");

        _logger.LogInformation("Connecting to {EndPoint}...", _remoteEndPoint);
        
        _tcpClient = new TcpClient();
        switch (_remoteEndPoint)
        {
            case IPEndPoint ipEndPoint:
                await _tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port, cancellationToken);
                break;
            case DnsEndPoint dnsEndPoint:
                await _tcpClient.ConnectAsync(dnsEndPoint.Host, dnsEndPoint.Port, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Не поддерживаемый тип EndPoint: {_remoteEndPoint.GetType().Name}");
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
        ConnectAckParameters = ackParams;
        foreach (var (key, value) in ackParams)
        {
            _logger.LogDebug("ConnectAck параметр: {Key} = {Value}", key, value);
        }

        if (ackParams.TryGetValue("nipple.swp.connection.keep_alive.timeout", out var kaTimeoutObj) && kaTimeoutObj is string kaTimeoutStr && int.TryParse(kaTimeoutStr, out var kaTimeoutSecs))
        {
            KeepAliveTimeout = TimeSpan.FromSeconds(kaTimeoutSecs);
        }

        _logger.LogInformation("Connected successfully. Keep-alive timeout: {Timeout}", KeepAliveTimeout);

        // Start background loops
        _readLoopTask = Task.Run(() => ReadLoopAsync(_connectionCts.Token));
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_connectionCts.Token));
    }

    public async Task<RasEndpoint> OpenEndpointAsync(ServiceId serviceId, Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<RasEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingOpenEndpoints.TryAdd(serviceId, tcs))
        {
            throw new InvalidOperationException($"Already opening endpoint for service: {serviceId}");
        }

        try
        {
            var openParams = parameters ?? new Dictionary<string, object?>();
            if (!openParams.ContainsKey("endpoint.encoding"))
            {
                openParams["endpoint.encoding"] = "deflate";
            }

            var bufferWriter = new ArrayBufferWriter<byte>();
            var writer = new RasWriter(bufferWriter);
            
            // Format ServiceId
            writer.WriteString(serviceId.Name);
            writer.WriteString(serviceId.Version);
            // Format Parameters
            RasParametersSerializer.WriteParameters(ref writer, openParams);

            await WriteFrameAsync((byte)SwpMessageType.EndpointOpen, bufferWriter.WrittenMemory, cancellationToken);

            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            var endpoint = await tcs.Task;
            _activeEndpoints[endpoint.EndpointId] = endpoint;
            return endpoint;
        }
        catch
        {
            _pendingOpenEndpoints.TryRemove(serviceId, out _);
            throw;
        }
    }

    public async Task CloseEndpointAsync(int endpointId, CancellationToken cancellationToken = default)
    {
        _activeEndpoints.TryRemove(endpointId, out _);

        var bufferWriter = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(bufferWriter);
        writer.WriteEndpointId(endpointId);

        await WriteFrameAsync((byte)SwpMessageType.EndpointClose, bufferWriter.WrittenMemory, cancellationToken);
    }

    public async Task<byte[]> SendEndpointMessageAsync(int endpointId, ReadOnlyMemory<byte> payload, bool useCompression, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(endpointId, tcs))
        {
            throw new InvalidOperationException($"A request is already pending on endpoint {endpointId}");
        }

        bool sent = false;
        try
        {
            // 1. Build endpoint body: [0x01 (MESSAGE)][payload]
            var bodyWriter = new ArrayBufferWriter<byte>(1 + payload.Length);
            var bodyRasWriter = new RasWriter(bodyWriter);
            bodyRasWriter.WriteByte(0x01); // MESSAGE
            bodyRasWriter.WriteBytes(payload.Span);

            ReadOnlyMemory<byte> endpointBody = bodyWriter.WrittenMemory;
            short format = 0;

            if (useCompression)
            {
                endpointBody = RasCompression.Deflate(endpointBody.Span);
                format = 1; // bit 0 = deflate
            }

            // 2. Build SWP frame body: [endpointId: nullable_size][format: short BE][endpointBody]
            var frameBodyWriter = new ArrayBufferWriter<byte>(10 + endpointBody.Length);
            var frameRasWriter = new RasWriter(frameBodyWriter);
            frameRasWriter.WriteEndpointId(endpointId);
            frameRasWriter.WriteShort(format);
            frameRasWriter.WriteBytes(endpointBody.Span);

            // 3. Send frame
            await WriteFrameAsync((byte)SwpMessageType.EndpointMessage, frameBodyWriter.WrittenMemory, cancellationToken);
            sent = true;

            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task;
        }
        catch
        {
            _pendingRequests.TryRemove(endpointId, out _);
            // Опасен только отменённый ПОСЛЕ отправки запрос: ответ сервера станет «осиротевшим»
            // и будет ошибочно принят за следующий. Faulted tcs (серверная ошибка) или обрыв —
            // нормальные исходы, рассинхрона нет.
            if (sent && tcs.Task.IsCanceled)
            {
                Volatile.Write(ref _closed, 1);
                FailAllPending(new RasException("Запрос отменён после отправки — соединение закрыто во избежание рассинхронизации"));
                DisposeAsynchronously();
            }
            throw;
        }
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

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
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
        finally
        {
            _writeGate.Release();
        }
    }

    public ValueTask<SwpFrame> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        return _unhandledFrames.Reader.ReadAsync(cancellationToken);
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
                    DispatchIncomingFrame(frame);
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
            Volatile.Write(ref _closed, 1);
            _unhandledFrames.Writer.TryComplete();
            FailAllPending(new EndOfStreamException("Connection closed"));
            await DisposeAsync();
        }
    }

    private void DispatchIncomingFrame(SwpFrame frame)
    {
        switch ((SwpMessageType)frame.Type)
        {
            case SwpMessageType.KeepAlive:
                // Ignore KeepAlive
                break;

            case SwpMessageType.EndpointOpenAck:
                HandleEndpointOpenAck(frame.Payload);
                break;

            case SwpMessageType.EndpointFailure:
                HandleEndpointFailure(frame.Payload);
                break;

            case SwpMessageType.EndpointMessage:
                HandleEndpointMessage(frame.Payload);
                break;

            case SwpMessageType.Disconnect:
                HandleDisconnect(frame.Payload);
                break;

            default:
                _unhandledFrames.Writer.TryWrite(frame);
                break;
        }
    }

    private void HandleEndpointOpenAck(byte[] payload)
    {
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);
        
        string? name = reader.ReadString();
        string? version = reader.ReadString();
        int? endpointId = reader.ReadEndpointId();
        var parameters = RasParametersSerializer.ReadParameters(ref reader);

        if (name is not null && version is not null && endpointId is not null)
        {
            var serviceId = new ServiceId(name, version);
            if (_pendingOpenEndpoints.TryRemove(serviceId, out var tcs))
            {
                bool useCompression = parameters.TryGetValue("endpoint.encoding", out var enc) && enc is string s && s.Equals("deflate", StringComparison.OrdinalIgnoreCase);
                var endpoint = new RasEndpoint(endpointId.Value, serviceId, this, useCompression);
                tcs.TrySetResult(endpoint);
            }
            else
            {
                // Open без ожидающей заявки (гонка отмены): эндпоинт на сервере осиротел;
                // соединение в этом сценарии закрывается guard-ом cancel-after-send.
                _logger.LogWarning("EndpointOpenAck для неизвестного сервиса {Service} (id={Id}) — осиротевший эндпоинт", serviceId, endpointId);
            }
        }
    }

    private void HandleEndpointFailure(byte[] payload)
    {
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);

        string? name = reader.ReadString();
        string? version = reader.ReadString();
        int? endpointId = reader.ReadEndpointId();
        Exception? cause = RasThrowableSerializer.ReadThrowable(ref reader) ?? new RasException("Unknown endpoint failure");

        if (name is not null && version is not null)
        {
            var serviceId = new ServiceId(name, version);
            if (_pendingOpenEndpoints.TryRemove(serviceId, out var openTcs))
            {
                openTcs.TrySetException(cause);
            }
        }

        if (endpointId is not null)
        {
            if (_pendingRequests.TryRemove(endpointId.Value, out var reqTcs))
            {
                reqTcs.TrySetException(cause);
            }
        }
    }

    private void HandleEndpointMessage(byte[] payload)
    {
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);

        int? endpointId = reader.ReadEndpointId();
        if (endpointId is null) return;

        short format = reader.ReadShort();
        
        // Remaining bytes
        byte[] body = reader.ReadRemainingBytes();

        if ((format & 1) != 0)
        {
            body = RasCompression.Inflate(body);
        }

        if (body.Length == 0) return;

        byte kind = body[0];
        ReadOnlyMemory<byte> content = body.AsMemory(1);

        if (_pendingRequests.TryRemove(endpointId.Value, out var tcs))
        {
            switch (kind)
            {
                case 0x00: // VOID
                    tcs.TrySetResult(Array.Empty<byte>());
                    break;
                case 0x01: // MESSAGE
                    tcs.TrySetResult(content.ToArray());
                    break;
                case 0xFF: // EXCEPTION
                    var errSeq = new ReadOnlySequence<byte>(content);
                    var errReader = new RasReader(errSeq);
                    var exception = RasThrowableSerializer.ReadThrowable(ref errReader) ?? new RasException("Remote exception");
                    tcs.TrySetException(exception);
                    break;
                default:
                    tcs.TrySetException(new InvalidDataException($"Unknown endpoint message kind: {kind}"));
                    break;
            }
        }
    }

    private void HandleDisconnect(byte[] payload)
    {
        Exception cause;
        if (payload.Length > 0)
        {
            var seq = new ReadOnlySequence<byte>(payload);
            var reader = new RasReader(seq);
            cause = RasThrowableSerializer.ReadThrowable(ref reader) ?? new RasException("Disconnected by remote host");
        }
        else
        {
            cause = new RasException("Disconnected by remote host");
        }

        // Сервер объявил конец соединения (Java: failure connectFuture + channel.close()).
        Volatile.Write(ref _closed, 1);
        _logger.LogWarning(cause, "Сервер прислал DISCONNECT — соединение закрывается");
        FailAllPending(cause);
        DisposeAsynchronously();
    }

    private void FailAllPending(Exception ex)
    {
        // TryRemove ДО SetException: иначе заявка, добавленная в момент перебора, была бы
        // удалена Clear()-ом незавершённой и её ожидающий повис бы до таймаута.
        foreach (var key in _pendingOpenEndpoints.Keys.ToArray())
        {
            if (_pendingOpenEndpoints.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
        }

        foreach (var key in _pendingRequests.Keys.ToArray())
        {
            if (_pendingRequests.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        if (KeepAliveTimeout <= TimeSpan.Zero) return;

        try
        {
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
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

        if (size < 0 || size > RasWireConstants.MaxFrameSize)
            throw new RasProtocolException($"SWP frame size out of bounds: {size}");

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

    /// <summary>
    /// Жёсткий разрыв TCP (RST) без protocol-level закрытия — только для хаос/нагрузочных тестов
    /// проверки устойчивости сервера к «неправильным» клиентам.
    /// </summary>
    public void Abort()
    {
        Volatile.Write(ref _closed, 1);
        _connectionCts.Cancel();
        _reader?.Complete();
        _writer?.Complete();
        try
        {
            if (_tcpClient?.Client is { Connected: true } socket)
            {
                try { socket.LingerState = new System.Net.Sockets.LingerOption(true, 0); }
                catch (SocketException) { /* сокет умирает — нормально */ }
            }
            _networkStream?.Dispose();
            _tcpClient?.Dispose();
        }
        catch (SocketException) { /* сокет уже мёртв */ }
        catch (ObjectDisposedException) { /* соединение уже закрыто (read-loop) */ }
    }

    private void DisposeAsynchronously()
        => _ = Task.Run(async () =>
        {
            try { await DisposeAsync(); } catch { /* best effort */ }
        });

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _closed, 1);
        try { _connectionCts.Cancel(); } catch (ObjectDisposedException) { }

        _reader?.Complete();
        _writer?.Complete();
        try { _networkStream?.Dispose(); } catch (ObjectDisposedException) { }
        try { _tcpClient?.Dispose(); } catch (ObjectDisposedException) { }
        
        if (_readLoopTask is not null)
        {
            try { await Task.WhenAny(_readLoopTask, Task.Delay(500)); } catch { }
        }
        if (_keepAliveTask is not null)
        {
            try { await Task.WhenAny(_keepAliveTask, Task.Delay(500)); } catch { }
        }

        try { _connectionCts.Dispose(); } catch (ObjectDisposedException) { }
    }
}

public readonly record struct SwpFrame(byte Type, byte[] Payload);

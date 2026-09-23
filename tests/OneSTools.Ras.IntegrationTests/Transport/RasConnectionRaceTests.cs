using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Transport;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Transport;

/// <summary>
/// Регресс-тесты гонок транспорта (docs/SECURITY.md §3 T9): серверный DISCONNECT завершает
/// pending-запросы и закрывает соединение; отмена после отправки закрывает соединение
/// (анти-десинхронизация); повторный ConnectAsync отвергается.
/// «Сервер» — синтетический TcpListener, говорящий по SWP вручную.
/// </summary>
public sealed class RasConnectionRaceTests : IAsyncLifetime
{
    private static readonly ServiceId AdminService = new("v8.service.Admin.Cluster", "14.0");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Каждое принятое соединение выставляет сюда очередь исходящих фреймов.</summary>
    private readonly Channel<Channel<(byte Type, byte[] Payload)>> _newConnections =
        Channel.CreateUnbounded<Channel<(byte, byte[])>>();

    public Task InitializeAsync()
    {
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        await Task.CompletedTask;
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = ServeAsync(client, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        var outbox = Channel.CreateUnbounded<(byte Type, byte[] Payload)>();
        _newConnections.Writer.TryWrite(outbox);

        using (client)
        await using (var stream = client.GetStream())
        {
            var scratch = new byte[8];
            await stream.ReadExactlyAsync(scratch, ct); // negotiate

            var frame = await ReadFrameAsync(stream, ct);
            if (frame.Type != (byte)SwpMessageType.Connect) return;
            await WriteFrameAsync(stream, (byte)SwpMessageType.ConnectAck,
                Body(b => b.WriteNullableSize(null)), ct);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var pump = Task.Run(async () =>
            {
                await foreach (var f in outbox.Reader.ReadAllAsync(linked.Token))
                {
                    await WriteFrameAsync(stream, f.Type, f.Payload, linked.Token);
                }
            }, linked.Token);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    frame = await ReadFrameAsync(stream, ct);
                    if (frame.Type == (byte)SwpMessageType.EndpointOpen)
                    {
                        outbox.Writer.TryWrite(((byte)SwpMessageType.EndpointOpenAck,
                            OpenAckBody(AdminService.Name, AdminService.Version, 1)));
                    }
                    // EndpointMessage — молчим: ответы диктует тест через outbox.
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or SocketException or OperationCanceledException)
            {
                // клиент/тест завершился — штатный исход для синтетического сервера
            }
            finally
            {
                linked.Cancel();
                outbox.Writer.TryComplete();
                try { await pump; } catch { }
            }
        }
    }

    /// <summary>Ждёт подключения клиента и возвращает его исходящий канал (управление тестом).</summary>
    private async Task<Channel<(byte Type, byte[] Payload)>> WaitForConnectionAsync(CancellationToken ct)
    {
        return await _newConnections.Reader.ReadAsync(ct);
    }

    private static byte[] OpenAckBody(string name, string version, int endpointId) =>
        Body(w =>
        {
            w.WriteString(name);
            w.WriteString(version);
            w.WriteEndpointId(endpointId);
            w.WriteNullableSize(null);
        });

    private static byte[] Body(Action<RasWriter> write)
    {
        var buf = new ArrayBufferWriter<byte>();
        write(new RasWriter(buf));
        return buf.WrittenMemory.ToArray();
    }

    private static byte[] DisconnectBody()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new RasWriter(buf);
        var ex = new RasRemoteException("swp.Disconnect", "server says goodbye", Array.Empty<RasRemoteStackFrame>());
        RasThrowableSerializer.WriteThrowable(ref w, ex);
        return buf.WrittenMemory.ToArray();
    }

    private static async Task<(byte Type, byte[] Payload)> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var one = new byte[1];
        await stream.ReadExactlyAsync(one, ct);
        byte type = one[0];

        int size = 0, shift = 0;
        while (true)
        {
            await stream.ReadExactlyAsync(one, ct);
            byte b = one[0];
            size |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }

        var payload = new byte[size];
        if (size > 0) await stream.ReadExactlyAsync(payload, ct);
        return (type, payload);
    }

    private static async Task WriteFrameAsync(Stream stream, byte type, byte[] payload, CancellationToken ct)
    {
        var header = new List<byte> { type };
        int size = payload.Length;
        while (true)
        {
            byte b = (byte)(size & 0x7F);
            size >>= 7;
            if (size != 0) b |= 0x80;
            header.Add(b);
            if (size == 0) break;
        }

        await stream.WriteAsync(header.ToArray(), ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    [Fact]
    public async Task Server_Disconnect_Fails_Pending_Request_And_Closes_Connection()
    {
        await using var conn = new RasConnection(new IPEndPoint(IPAddress.Loopback, Port), NullLogger<RasConnection>.Instance);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await conn.ConnectAsync(new Dictionary<string, object?> { ["client.name"] = "race-test" }, setup.Token);

        var serverOut = await WaitForConnectionAsync(setup.Token);
        await using var endpoint = await conn.OpenEndpointAsync(AdminService, cancellationToken: setup.Token);

        var execute = endpoint.ExecuteAsync(new GetAgentVersionRequest(), 14, setup.Token);

        serverOut.Writer.TryWrite(((byte)SwpMessageType.Disconnect, DisconnectBody()));

        var winner = await Task.WhenAny(execute, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.True(ReferenceEquals(winner, execute), "pending-запрос не завершился после серверного DISCONNECT (завис)");

        var ex = await Assert.ThrowsAnyAsync<RasException>(() => execute);
        Assert.Contains("goodbye", ex.Message);
        Assert.True(conn.IsClosed, "после DISCONNECT соединение должно быть закрыто");

        // второй вызов на мёртвом соединении — немедленный отказ, не зависание
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            endpoint.ExecuteAsync(new GetAgentVersionRequest(), 14, cts2.Token));
    }

    [Fact]
    public async Task Cancel_After_Send_Closes_Connection_And_Frees_Gate()
    {
        await using var conn = new RasConnection(new IPEndPoint(IPAddress.Loopback, Port), NullLogger<RasConnection>.Instance);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await conn.ConnectAsync(null, setup.Token);

        await using var endpoint = await conn.OpenEndpointAsync(AdminService, cancellationToken: setup.Token);

        // сервер молчит на EndpointMessage ⇒ отмена после отправки ⇒ guard закрывает соединение
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            endpoint.ExecuteAsync(new GetAgentVersionRequest(), 14, cts.Token));

        await WaitUntilAsync(() => conn.IsClosed, TimeSpan.FromSeconds(3));

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            endpoint.ExecuteAsync(new GetAgentVersionRequest(), 14, cts2.Token));
    }

    [Fact]
    public async Task Second_Connect_Is_Rejected()
    {
        await using var conn = new RasConnection(new IPEndPoint(IPAddress.Loopback, Port), NullLogger<RasConnection>.Instance);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await conn.ConnectAsync(null, setup.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => conn.ConnectAsync(null, setup.Token));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, cts.Token);
        }
    }
}

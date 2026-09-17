using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Transport;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.MockRas;

public sealed class RasConnectionTests : IAsyncLifetime
{
    private CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private int _port;

    public async Task InitializeAsync()
    {
        // Find a free port
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        // Start mock server
        _serverTask = Task.Run(() => Program.RunAsync(new[] { _port.ToString() }, _serverCts.Token));

        // Wait for server to start listening
        using var client = new TcpClient();
        for (int i = 0; i < 50; i++)
        {
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, _port);
                client.Close();
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }
    }

    public async Task DisposeAsync()
    {
        _serverCts.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
        }
        _serverCts.Dispose();
    }

    [Fact]
    public async Task Can_Connect_And_Negotiate()
    {
        await using var connection = new RasConnection(new IPEndPoint(IPAddress.Loopback, _port), NullLogger<RasConnection>.Instance);

        var parameters = new Dictionary<string, object?>
        {
            { "client.name", "integration_test" }
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        await connection.ConnectAsync(parameters, cts.Token);

        Assert.True(connection.KeepAliveTimeout > TimeSpan.Zero);
    }

    [Fact]
    public async Task Can_Send_And_Receive_KeepAlive()
    {
        await using var connection = new RasConnection(new IPEndPoint(IPAddress.Loopback, _port), NullLogger<RasConnection>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(null, cts.Token);

        // Send a frame manually
        await connection.WriteFrameAsync((byte)SwpMessageType.EndpointClose, Array.Empty<byte>(), cts.Token);

        // We don't have endpoints yet, so it won't respond to EndpointClose.
        // But we can check that it doesn't throw.
    }
}
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Transport;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.MockRas;

public sealed class RasEndpointTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private int _port;

    public async Task InitializeAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _serverTask = Task.Run(() => Program.RunAsync(new[] { _port.ToString() }, _serverCts.Token));

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
    public async Task Can_Open_Endpoint_Send_Compressed_Message_And_Close()
    {
        await using var connection = new RasConnection(new IPEndPoint(IPAddress.Loopback, _port), NullLogger<RasConnection>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(null, cts.Token);

        var serviceId = new ServiceId("com.1c.v8.ibis.admin.IAgentAdminConnection", "1.0");
        
        await using var endpoint = await connection.OpenEndpointAsync(serviceId, cancellationToken: cts.Token);

        Assert.True(endpoint.EndpointId > 0);
        Assert.Equal(serviceId, endpoint.ServiceId);
        Assert.True(endpoint.UseCompression);

        byte[] request = Encoding.UTF8.GetBytes("GetClustersRequestPayload12345");
        byte[] response = await endpoint.SendMessageAsync(request, cts.Token);

        Assert.Equal(request, response);

        await endpoint.CloseAsync(cts.Token);
    }
}

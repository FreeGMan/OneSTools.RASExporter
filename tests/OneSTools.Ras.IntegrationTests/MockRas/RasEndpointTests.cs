using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Transport;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Wire;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.MockRas;

public sealed class RasEndpointTests : IAsyncLifetime
{
    private sealed record WrongResponseRequest : IRasRequest<GetClustersResponse>
    {
        public MessageType MessageType => MessageType.GetAgentAdminsRequest;
        public void Serialize(ref RasWriter writer, int version = 0) { }
    }

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

        var serviceId = new ServiceId("v8.service.Admin.Cluster", "14.0");
        
        await using var endpoint = await connection.OpenEndpointAsync(serviceId, cancellationToken: cts.Token);

        Assert.True(endpoint.EndpointId > 0);
        Assert.Equal(serviceId, endpoint.ServiceId);
        Assert.True(endpoint.UseCompression);

        // реальный запрос через транспортно-сжатый эндпоинт: [135 GetAgentVersionRequest]
        byte[] request = new byte[] { (byte)OneSTools.Ras.Client.Messages.MessageType.GetAgentVersionRequest };
        byte[] response = await endpoint.SendMessageAsync(request, cts.Token);

        Assert.True(response.Length > 1);
        Assert.Equal((byte)OneSTools.Ras.Client.Messages.MessageType.GetAgentVersionResponse, response[0]);

        await endpoint.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task Can_Open_Endpoint_Send_Uncompressed_Message_And_Close()
    {
        await using var connection = new RasConnection(new IPEndPoint(IPAddress.Loopback, _port), NullLogger<RasConnection>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(null, cts.Token);

        var serviceId = new ServiceId("v8.service.Admin.Cluster", "14.0");
        var parameters = new Dictionary<string, object?>
        {
            { "endpoint.encoding", "none" }
        };

        await using var endpoint = await connection.OpenEndpointAsync(serviceId, parameters, cancellationToken: cts.Token);

        Assert.True(endpoint.EndpointId > 0);
        Assert.Equal(serviceId, endpoint.ServiceId);
        Assert.False(endpoint.UseCompression);

        byte[] request = new byte[] { (byte)OneSTools.Ras.Client.Messages.MessageType.GetAgentVersionRequest };
        byte[] response = await endpoint.SendMessageAsync(request, cts.Token);

        Assert.True(response.Length > 1);
        Assert.Equal((byte)OneSTools.Ras.Client.Messages.MessageType.GetAgentVersionResponse, response[0]);

        await endpoint.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task Typed_Request_Rejects_Unexpected_Response_Type()
    {
        await using var connection = new RasConnection(new IPEndPoint(IPAddress.Loopback, _port));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(cancellationToken: timeout.Token);
        await using var endpoint = await connection.OpenEndpointAsync(
            new ServiceId("v8.service.Admin.Cluster", "14.0"), cancellationToken: timeout.Token);

        await Assert.ThrowsAsync<RasProtocolException>(() =>
            endpoint.ExecuteAsync(new WrongResponseRequest(), 14, timeout.Token));
    }
}

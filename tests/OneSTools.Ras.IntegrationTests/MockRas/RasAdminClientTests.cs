using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.MockRas;

public sealed class RasAdminClientTests : IAsyncLifetime
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
    public async Task Can_Connect_And_Execute_Monitoring_Queries()
    {
        await using var client = new RasAdminClient("127.0.0.1", _port, NullLogger<RasAdminClient>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await client.ConnectAsync("admin", "pwd", cancellationToken: cts.Token);

        // 1. GetClusters
        var clusters = await client.GetClustersAsync(cts.Token);
        Assert.Single(clusters);
        var cluster = clusters[0];
        Assert.Equal("Mock-Cluster-01", cluster.Name);

        // 2. Authenticate Cluster
        await client.AuthenticateClusterAsync(cluster.Uuid, "clusterAdmin", "clusterPwd", cts.Token);

        // 3. GetSessions
        var sessions = await client.GetSessionsAsync(cluster.Uuid, cts.Token);
        Assert.Single(sessions);
        Assert.Equal("TestUser", sessions[0].UserName);

        // 4. GetProcesses
        var processes = await client.GetWorkingProcessesAsync(cluster.Uuid, cts.Token);
        Assert.Single(processes);
        Assert.Equal("1001", processes[0].Pid);

        // 5. GetConnections
        var connections = await client.GetConnectionsShortAsync(cluster.Uuid, cts.Token);
        Assert.Single(connections);
        Assert.Equal(101, connections[0].Id);

        // 6. GetLocks
        var locks = await client.GetLocksAsync(cluster.Uuid, cts.Token);
        Assert.Single(locks);
        Assert.Equal("Mock Lock", locks[0].Description);

        // 7. GetInfoBases
        var infobases = await client.GetInfoBasesShortAsync(cluster.Uuid, cts.Token);
        Assert.Single(infobases);
        Assert.Equal("mock_db", infobases[0].Name);

        // 8. GetAgentVersion
        var version = await client.GetAgentVersionAsync(cts.Token);
        Assert.Equal("8.3.24.1342", version);
    }
}

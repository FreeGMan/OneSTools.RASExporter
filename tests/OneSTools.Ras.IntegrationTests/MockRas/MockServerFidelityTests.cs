using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Transport;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.MockRas;

/// <summary>
/// Проверка protocol-fidelity мока: имя/версии сервиса, отказ UnsupportedService
/// со списком supported=, negotiation клиента и гейт прав cluster-admin — всё как
/// на реальном RAS 8.3.25 (TODO.md п.7).
/// </summary>
public sealed class MockServerFidelityTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _serverCts = new();
    private readonly List<Task> _serverTasks = new();
    private int _fullPort;
    private int _cappedPort;

    public async Task InitializeAsync()
    {
        _fullPort = await StartMockAsync(maxAdminVersion: "14");
        _cappedPort = await StartMockAsync(maxAdminVersion: "11");
    }

    public async Task DisposeAsync()
    {
        _serverCts.Cancel();
        foreach (var t in _serverTasks)
        {
            try { await t; } catch { }
        }
        _serverCts.Dispose();
    }

    private async Task<int> StartMockAsync(string maxAdminVersion)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _serverTasks.Add(Task.Run(() => Program.RunAsync(new[] { port.ToString(), maxAdminVersion }, _serverCts.Token)));

        using var probe = new TcpClient();
        for (int i = 0; i < 50; i++)
        {
            try
            {
                await probe.ConnectAsync(IPAddress.Loopback, port);
                return port;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }

        throw new InvalidOperationException("Mock server did not start");
    }

    [Fact]
    public async Task FQ_Service_Name_Is_Rejected_With_UnsupportedService()
    {
        await using var connection = new RasConnection(new IPEndPoint(IPAddress.Loopback, _fullPort), NullLogger<RasConnection>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.ConnectAsync(null, cts.Token);

        var wrong = new ServiceId("com.1c.v8.ibis.admin.IAgentAdminConnection", "1.0");
        var ex = await Assert.ThrowsAsync<RasRemoteException>(() => connection.OpenEndpointAsync(wrong, cancellationToken: cts.Token));
        Assert.Contains("UnsupportedService", ex.Message + ex.InnerException?.Message);
    }

    [Fact]
    public async Task Client_Negotiates_Latest_Common_Version()
    {
        await using var client = new RasAdminClient("localhost", _fullPort, NullLogger<RasAdminClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await client.ConnectAsync(cancellationToken: cts.Token);

        Assert.Equal(14, client.ProtocolVersion);
        Assert.Equal(RasAdminClient.AdminServiceName, client.Endpoint!.ServiceId.Name);
    }

    [Fact]
    public async Task Client_Negotiation_Falls_Back_Per_Server_Supported_List()
    {
        await using var client = new RasAdminClient("localhost", _cappedPort, NullLogger<RasAdminClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await client.ConnectAsync(cancellationToken: cts.Token);

        Assert.Equal(11, client.ProtocolVersion);
    }

    [Fact]
    public async Task Concurrent_Calls_On_One_Client_Are_Serialized_By_Endpoint_Gate()
    {
        await using var client = new RasAdminClient("localhost", _fullPort, NullLogger<RasAdminClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(cancellationToken: cts.Token);

        var clusters = await client.GetClustersAsync(cts.Token);
        var clusterId = clusters[0].Uuid;
        await client.AuthenticateClusterAsync(clusterId, "", "", cts.Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.GetSessionsAsync(clusterId, cts.Token)));

        Assert.All(results, r => Assert.Single(r));
    }

    [Fact]
    public async Task Concurrent_Mixed_Operations_All_Succeed()
    {
        // Регресс на «эхо-фолбэк» мока: неохваченные операции (getClusterInfo, getInfoBases,
        // servers, managers, admins…) возвращали эхо запроса, клиент ломался на мусоре.
        await using var clientA = new RasAdminClient("localhost", _fullPort, NullLogger<RasAdminClient>.Instance);
        await using var clientB = new RasAdminClient("localhost", _fullPort, NullLogger<RasAdminClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await Task.WhenAll(clientA.ConnectAsync(cancellationToken: cts.Token), clientB.ConnectAsync(cancellationToken: cts.Token));
        foreach (var c in new[] { clientA, clientB })
            await c.AuthenticateClusterAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), "", "", cts.Token);

        var rounds = new[] { clientA, clientB }.SelectMany(c => new Func<Task>[]
        {
            () => c.GetClustersAsync(cts.Token),
            () => c.GetClusterInfoAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetWorkingServersAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetClusterManagersAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetClusterServicesAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetAgentAdminsAsync(cts.Token),
            () => c.GetClusterAdminsAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetSessionsAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetInfoBasesAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetWorkingProcessesAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetLocksAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), cts.Token),
            () => c.GetAgentVersionAsync(cts.Token),
        }).ToList();

        // перемешиваем порядок для двух клиентов
        var tasksA = rounds.Where((_, i) => i % 2 == 0).Select(f => RunAndUnwrap(f)).ToArray();
        var tasksB = rounds.Where((_, i) => i % 2 == 1).Select(f => RunAndUnwrap(f)).ToArray();
        await Task.WhenAll(tasksA.Concat(tasksB));

        static async Task RunAndUnwrap(Func<Task> f) => await f();
    }

    [Fact]
    public async Task Cluster_Queries_Without_Authentication_Get_Rights_Exception()
    {
        await using var client = new RasAdminClient("localhost", _fullPort, NullLogger<RasAdminClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(cancellationToken: cts.Token);

        var clusters = await client.GetClustersAsync(cts.Token);
        var clusterId = clusters[0].Uuid;

        var ex = await Assert.ThrowsAsync<RasRemoteException>(() => client.GetSessionsAsync(clusterId, cts.Token));
        Assert.Contains("Rights", ex.Message);

        // после анонимной аутентификации — проходит
        await client.AuthenticateClusterAsync(clusterId, "", "", cts.Token);
        var sessions = await client.GetSessionsAsync(clusterId, cts.Token);
        Assert.NotEmpty(sessions);
    }
}

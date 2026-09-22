using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.IntegrationTests.RealRas;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.RealRas;

/// <summary>
/// Тесты кластерного API против «почти реального» RAS: контейнер 1c-test-server,
/// RAS подключен к рабочему ragent, на сервере нет ни одной информационной базы.
/// Кластер без настроенной аутентификации (SecurityLevel=0), но RAS всё равно
/// требует явный вызов authenticateCluster (пустыми учётками) перед методами
/// просмотра сессий/процессов/серверов — это и проверяется.
/// </summary>
public sealed class RealClusterRasTests
{
    private static string? ClusterIdOverride => Environment.GetEnvironmentVariable("RAC_TEST_RAS_CLUSTER_ID");

    private static async Task<(Client.Transport.RasConnection, Client.Transport.RasEndpoint, Guid, int)> OpenAuthenticatedClusterAsync(CancellationTokenSource cts)
    {
        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);

        var clusters = await endpoint.ExecuteAsync(new GetClustersRequest(), endpoint.ProtoVersion(), cts.Token);
        Assert.NotEmpty(clusters.Clusters);

        ClusterInfo cluster =
            ClusterIdOverride is string s && Guid.TryParse(s, out var @override)
                ? clusters.Clusters.First(c => c.Uuid == @override)
                : clusters.Clusters[0];

        try
        {
            await endpoint.ExecuteVoidAsync(new AuthenticateRequest(cluster.Uuid, "", ""), endpoint.ProtoVersion(), cts.Token);
        }
        catch (RasRemoteException ex)
        {
            await connection.DisposeAsync();
            await endpoint.DisposeAsync();
            throw new SkipException($"Кластер {cluster.Name} требует реальные учётные данные администратора (задайте RAC_TEST_RAS_CLUSTER_ID и доработка тестов): {ex.Message}");
        }

        return (connection, endpoint, cluster.Uuid, endpoint.ProtoVersion());
    }

    [SkippableFact]
    public async Task GetClusterInfo_Decodes()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var info = await endpoint.ExecuteAsync(new GetClusterInfoRequest(clusterId), ver, cts.Token);
            Assert.Equal(clusterId, info.Info.Uuid);
            Assert.False(string.IsNullOrWhiteSpace(info.Info.Host));
        }
    }

    [SkippableFact]
    public async Task GetInfoBases_Returns_Empty_List_On_Clean_Server()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var infobases = await endpoint.ExecuteAsync(new GetInfoBasesRequest(clusterId), ver, cts.Token);
            Assert.Empty(infobases.InfoBases);

            var shortList = await endpoint.ExecuteAsync(new GetInfoBasesShortRequest(clusterId), ver, cts.Token);
            Assert.Empty(shortList.InfoBases);
        }
    }

    [SkippableFact]
    public async Task GetSessions_Returns_Empty_List_No_Infobases()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var sessions = await endpoint.ExecuteAsync(new GetSessionsRequest(clusterId), ver, cts.Token);
            Assert.Empty(sessions.Sessions);
        }
    }

    [SkippableFact]
    public async Task GetWorkingProcesses_Decodes_RagentClusterAndServerProcesses()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var processes = await endpoint.ExecuteAsync(new GetWorkingProcessesRequest(clusterId), ver, cts.Token);
            Assert.NotEmpty(processes.Processes);
            foreach (var p in processes.Processes)
            {
                Assert.NotEqual(Guid.Empty, p.Uuid);
                Assert.False(string.IsNullOrWhiteSpace(p.Pid));
                Assert.False(string.IsNullOrWhiteSpace(p.Host));
                Assert.True(p.Port > 0);
            }
        }
    }

    [SkippableFact]
    public async Task GetWorkingServers_And_GetClusterManagers_Decode()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var servers = await endpoint.ExecuteAsync(new GetWorkingServersRequest(clusterId), ver, cts.Token);
            Assert.NotEmpty(servers.Servers);
            foreach (var s in servers.Servers)
            {
                Assert.NotEqual(Guid.Empty, s.Uuid);
                Assert.False(string.IsNullOrWhiteSpace(s.AgentHost));
                Assert.True(s.AgentPort > 0);
            }

            var managers = await endpoint.ExecuteAsync(new GetClusterManagersRequest(clusterId), ver, cts.Token);
            Assert.NotEmpty(managers.Managers);
            foreach (var m in managers.Managers)
            {
                Assert.NotEqual(Guid.Empty, m.Uuid);
                Assert.False(string.IsNullOrWhiteSpace(m.Host));
            }
        }
    }

    [SkippableFact]
    public async Task GetLocks_And_GetConnectionsShort_Decode()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var locks = await endpoint.ExecuteAsync(new GetLocksRequest(clusterId), ver, cts.Token);
            Assert.NotNull(locks.Locks);

            var connections = await endpoint.ExecuteAsync(new GetConnectionsShortRequest(clusterId), ver, cts.Token);
            // На пустом сервере соединения всё равно есть: служебные подключения RAS/агента к кластеру.
            Assert.NotNull(connections.Connections);
            foreach (var c in connections.Connections)
            {
                Assert.NotEqual(Guid.Empty, c.Uuid);
                Assert.False(string.IsNullOrWhiteSpace(c.Application));
                Assert.False(string.IsNullOrWhiteSpace(c.Host));
                Assert.True(c.ConnectedAt > DateTime.UnixEpoch);
            }
        }
    }

    [SkippableFact]
    public async Task GetClusterAdmins_Empty_With_Anonymous_Auth()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (connection, endpoint, clusterId, ver) = await OpenAuthenticatedClusterAsync(cts);
        await using (connection)
        await using (endpoint)
        {
            var admins = await endpoint.ExecuteAsync(new GetClusterAdminsRequest(clusterId), ver, cts.Token);
            Assert.NotNull(admins.Admins);
        }
    }

    [SkippableFact]
    public async Task AuthenticateCluster_With_Bad_Credentials_Fails_With_RasRemoteException()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            var clusters = await endpoint.ExecuteAsync(new GetClustersRequest(), endpoint.ProtoVersion(), cts.Token);
            var clusterId = clusters.Clusters[0].Uuid;

            var ex = await Assert.ThrowsAsync<RasRemoteException>(() =>
                endpoint.ExecuteVoidAsync(new AuthenticateRequest(clusterId, "nosuchadmin", "nosuchpwd"), endpoint.ProtoVersion(), cts.Token));

            Assert.Contains("Администратор", ex.Message);
        }
    }

    [SkippableFact]
    public async Task Cluster_Queries_Before_Authentication_Fail_With_Rights_Exception()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            var clusters = await endpoint.ExecuteAsync(new GetClustersRequest(), endpoint.ProtoVersion(), cts.Token);
            var clusterId = clusters.Clusters[0].Uuid;

            var ex = await Assert.ThrowsAsync<RasRemoteException>(() =>
                endpoint.ExecuteAsync(new GetSessionsRequest(clusterId), endpoint.ProtoVersion(), cts.Token));

            Assert.Contains("Rights", ex.Message);
        }
    }
}

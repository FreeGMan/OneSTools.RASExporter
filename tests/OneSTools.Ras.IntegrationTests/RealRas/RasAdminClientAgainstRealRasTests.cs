using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Exceptions;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.RealRas;

/// <summary>
/// Сквозная проверка публичного RasAdminClient против реального RAS (1c-test-server):
/// connect по DNS-имени (баг #1), negotiation сервиса v8.service.Admin.Cluster (баги #2/#3),
/// аутентификация в кластер и чтение метрик (баг #8 — пустые учётки обязательны).
/// </summary>
public sealed class RasAdminClientAgainstRealRasTests
{
    private static bool Reachable() => RealRasSupport.ServerReachable();

    [SkippableFact]
    public async Task Connects_Negotiates_And_Reads_Cluster_Version()
    {
        Skip.IfNot(Reachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var client = new RasAdminClient(RealRasSupport.Host, RealRasSupport.Port, NullLogger<RasAdminClient>.Instance);
        await client.ConnectAsync(cancellationToken: cts.Token);

        Assert.InRange(client.ProtocolVersion, 3, 99);
        Assert.Equal(RasAdminClient.AdminServiceName, client.Endpoint!.ServiceId.Name);

        var version = await client.GetAgentVersionAsync(cts.Token);
        Assert.Matches(@"^8\.\d+\.\d+\.\d+$", version);
    }

    [SkippableFact]
    public async Task Discovers_Clusters_Authenticates_Anonymously_And_Reads_Metrics()
    {
        Skip.IfNot(Reachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var client = new RasAdminClient(RealRasSupport.Host, RealRasSupport.Port, NullLogger<RasAdminClient>.Instance);
        await client.ConnectAsync(cancellationToken: cts.Token);

        var clusters = await client.GetClustersAsync(cts.Token);
        Assert.NotEmpty(clusters);
        var cluster = clusters[0];

        try
        {
            await client.AuthenticateClusterAsync(cluster.Uuid, "", "", cts.Token);
        }
        catch (RasRemoteException ex)
        {
            throw new SkipException($"Кластер требует реальные учётные данные: {ex.Message}");
        }

        var info = await client.GetClusterInfoAsync(cluster.Uuid, cts.Token);
        Assert.Equal(cluster.Uuid, info.Uuid);

        var sessions = await client.GetSessionsAsync(cluster.Uuid, cts.Token);
        Assert.NotNull(sessions);

        var processes = await client.GetWorkingProcessesAsync(cluster.Uuid, cts.Token);
        Assert.NotEmpty(processes);

        var servers = await client.GetWorkingServersAsync(cluster.Uuid, cts.Token);
        Assert.NotEmpty(servers);

        var managers = await client.GetClusterManagersAsync(cluster.Uuid, cts.Token);
        Assert.NotEmpty(managers);

        var infobases = await client.GetInfoBasesAsync(cluster.Uuid, cts.Token);
        Assert.NotNull(infobases);
    }

    [SkippableFact]
    public async Task Bad_Agent_Credentials_Propagate_Remote_Exception()
    {
        Skip.IfNot(Reachable(), $"RAS недоступен на {RealRasSupport.Host}:{RealRasSupport.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var client = new RasAdminClient(RealRasSupport.Host, RealRasSupport.Port, NullLogger<RasAdminClient>.Instance);

        await Assert.ThrowsAnyAsync<RasException>(() =>
            client.ConnectAsync("nosuchadmin", "badpwd", cancellationToken: cts.Token));
    }
}

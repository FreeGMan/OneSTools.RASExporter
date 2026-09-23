using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Transport;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.RealRas;

/// <summary>
/// Низкоуровневые протокольные тесты против РЕАЛЬНОГО RAS-сервера 1С 8.3
/// (стенд 1c-test-server по умолчанию, переопределение RAC_TEST_RAS_HOST/PORT).
/// Проверяют транспорт и форматы: handshake, connect/ack, negotiation эндпоинта,
/// версии сервиса, deflate, запросы без кластера и декодирование удалённых исключений.
/// Высокоуровневый клиент — RasAdminClientAgainstRealRasTests.
/// </summary>
public sealed class RealRasProtocolTests
{
    private static string Host => RealRasSupport.Host;
    private static int Port => RealRasSupport.Port;

    [SkippableFact]
    public async Task Handshake_And_Connect_Ack()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var connection = new RasConnection(RealRasSupport.ResolveServer(), NullLogger<RasConnection>.Instance);
        await connection.ConnectAsync(new Dictionary<string, object?> { { "client.name", "onestools_ras_protocol_test" } }, cts.Token);

        Assert.True(connection.KeepAliveTimeout > TimeSpan.Zero);
    }

    [SkippableFact]
    public async Task Connect_And_Open_Admin_Endpoint_With_Deflate()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            Assert.True(endpoint.EndpointId > 0);
            Assert.Equal(RealRasSupport.AdminServiceName, endpoint.ServiceId.Name);
            Assert.True(endpoint.UseCompression, "Сервер должен подтверждать endpoint.encoding=deflate");
        }
    }

    [SkippableFact]
    public async Task Open_Admin_Endpoint_Fails_With_Wrong_ServiceId()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var connection = new RasConnection(RealRasSupport.ResolveServer(), NullLogger<RasConnection>.Instance);
        await connection.ConnectAsync(null, cts.Token);

        var wrong = new ServiceId("com.1c.v8.ibis.admin.IAgentAdminConnection", "1.0");
        var ex = await Assert.ThrowsAsync<RasRemoteException>(() => connection.OpenEndpointAsync(wrong, cancellationToken: cts.Token));
        string full = ex.Message + " " + ex.InnerException?.Message;
        Assert.Contains("UnsupportedService", full);
    }

    [SkippableFact]
    public async Task GetAgentVersion_Returns_Platform_Version()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            var response = await endpoint.ExecuteAsync(new GetAgentVersionRequest(), protoVersion, cts.Token);

            Assert.Matches(@"^8\.\d+\.\d+\.\d+$", response.Version);
        }
    }

    [SkippableFact]
    public async Task GetClusters_Roundtrip_With_Real_Ragent_Or_Decoded_Exception()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            try
            {
                var clusters = await endpoint.ExecuteAsync(new GetClustersRequest(), protoVersion, cts.Token);
                Assert.NotNull(clusters.Clusters);
                foreach (var c in clusters.Clusters)
                {
                    Assert.NotEqual(Guid.Empty, c.Uuid);
                    Assert.False(string.IsNullOrWhiteSpace(c.Host));
                }
            }
            catch (RasRemoteException ex)
            {
                // RAS без живого ragent: осмысленное декодированное исключение вместо зависания
                Assert.Contains("cluster", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.IsType<RasRemoteException>(ex.InnerException);
            }
        }
    }

    [SkippableFact]
    public async Task AuthenticateAgent_With_Bad_Credentials_Fails_Cleanly()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            var ex = await Assert.ThrowsAsync<RasRemoteException>(() =>
                endpoint.ExecuteVoidAsync(new AuthenticateAgentRequest("nosuchuser", "badpwd"), protoVersion, cts.Token));

            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [SkippableFact]
    public async Task KeepAlive_Keeps_Connection_Alive()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            // Больше половины штатного keep-alive таймаута RAS (20 c) — соединение обязаны жить.
            await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);

            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            var response = await endpoint.ExecuteAsync(new GetAgentVersionRequest(), protoVersion, cts.Token);
            Assert.False(string.IsNullOrWhiteSpace(response.Version));
        }
    }

    [SkippableFact]
    public async Task Sequential_Requests_On_Same_Endpoint()
    {
        Skip.IfNot(RealRasSupport.ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var (connection, endpoint) = await RealRasSupport.OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            for (int i = 0; i < 20; i++)
            {
                var response = await endpoint.ExecuteAsync(new GetAgentVersionRequest(), protoVersion, cts.Token);
                Assert.False(string.IsNullOrWhiteSpace(response.Version));
            }
        }
    }
}

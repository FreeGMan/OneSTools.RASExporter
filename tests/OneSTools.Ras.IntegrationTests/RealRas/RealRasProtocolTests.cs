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
/// Протокольные тесты против РЕАЛЬНОГО RAS-сервера 1С 8.3 (контейнер `ras` из
/// docker-compose, адрес по умолчанию ras:1545, переопределение RAC_TEST_RAS_HOST/PORT).
/// RAS не подключён к рабочему кластеру ragent, поэтому проверяются транспорт и
/// codecs: handshake, connect/ack, открытие эндпоинта, версии сервиса, deflate,
/// запросы не требующие кластера и декодирование удаленных исключений.
/// </summary>
public sealed class RealRasProtocolTests
{
    // Реальный RAS регистрирует сервис администрирования под именем v8.service.Admin.Cluster
    // (Java SDK: AgentAdminConnectionProxy.SERVICE_NAME), версии 3.0..10.0, сервер 8.3.25
    // принимает и 12.0.
    private const string AdminServiceName = "v8.service.Admin.Cluster";
    private static readonly string[] CandidateVersions = { "12.0", "11.0", "10.0", "9.0", "8.0", "7.0", "6.0", "5.0", "4.0", "3.0" };

    private static readonly string Host = Environment.GetEnvironmentVariable("RAC_TEST_RAS_HOST") ?? "ras";
    private static readonly int Port = int.Parse(Environment.GetEnvironmentVariable("RAC_TEST_RAS_PORT") ?? "1545");

    private static bool ServerReachable()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(Host, Port).Wait(TimeSpan.FromSeconds(3));
        }
        catch (SocketException)
        {
            return false;
        }
    }

    // Известный баг (TODO.md #1): RasConnection.ConnectAsync не поддерживает DnsEndPoint —
    // InvalidCastException при приведении к IPEndPoint. Обход: резолвим IPv4 в тесте сами.
    private static IPEndPoint ResolveServer()
    {
        if (IPAddress.TryParse(Host, out var literal))
            return new IPEndPoint(literal, Port);
        var ips = Dns.GetHostAddresses(Host);
        var ipv4 = ips.First(a => a.AddressFamily == AddressFamily.InterNetwork);
        return new IPEndPoint(ipv4, Port);
    }

    private static async Task<(RasConnection Connection, RasEndpoint Endpoint)> OpenAdminEndpointAsync(CancellationToken cancellationToken)
    {
        var connection = new RasConnection(ResolveServer(), NullLogger<RasConnection>.Instance);
        await connection.ConnectAsync(new Dictionary<string, object?> { { "client.name", "onestools_ras_protocol_test" } }, cancellationToken);

        RasException? lastError = null;
        foreach (var version in CandidateVersions)
        {
            try
            {
                var endpoint = await connection.OpenEndpointAsync(new ServiceId(AdminServiceName, version), cancellationToken: cancellationToken);
                return (connection, endpoint);
            }
            catch (RasRemoteException ex)
            {
                lastError = ex;
            }
        }

        await connection.DisposeAsync();
        throw lastError ?? new RasException($"Ни одна версия {AdminServiceName} не открылась");
    }

    [SkippableFact]
    public async Task Handshake_And_Connect_Ack()
    {
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var connection = new RasConnection(ResolveServer(), NullLogger<RasConnection>.Instance);
        await connection.ConnectAsync(new Dictionary<string, object?> { { "client.name", "onestools_ras_protocol_test" } }, cts.Token);

        Assert.True(connection.KeepAliveTimeout > TimeSpan.Zero);
    }

    [SkippableFact]
    public async Task Connect_And_Open_Admin_Endpoint_With_Deflate()
    {
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            Assert.True(endpoint.EndpointId > 0);
            Assert.Equal(AdminServiceName, endpoint.ServiceId.Name);
            Assert.True(endpoint.UseCompression, "Сервер должен подтверждать endpoint.encoding=deflate");
        }
    }

    [SkippableFact]
    public async Task Open_Admin_Endpoint_Fails_With_Wrong_ServiceId()
    {
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var connection = new RasConnection(ResolveServer(), NullLogger<RasConnection>.Instance);
        await connection.ConnectAsync(null, cts.Token);

        var wrong = new ServiceId("com.1c.v8.ibis.admin.IAgentAdminConnection", "1.0");
        var ex = await Assert.ThrowsAsync<RasRemoteException>(() => connection.OpenEndpointAsync(wrong, cancellationToken: cts.Token));
        string full = ex.Message + " " + ex.InnerException?.Message;
        Assert.Contains("UnsupportedService", full);
    }

    [SkippableFact]
    public async Task GetAgentVersion_Returns_Platform_Version()
    {
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            var response = await endpoint.ExecuteAsync(new GetAgentVersionRequest(), protoVersion, cts.Token);

            Assert.Matches(@"^8\.\d+\.\d+\.\d+$", response.Version);
        }
    }

    [SkippableFact]
    public async Task GetClusters_Without_Ragent_Decodes_Remote_Exception()
    {
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await OpenAdminEndpointAsync(cts.Token);
        await using (connection)
        await using (endpoint)
        {
            int protoVersion = int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
            try
            {
                var clusters = await endpoint.ExecuteAsync(new GetClustersRequest(), protoVersion, cts.Token);
                Assert.NotNull(clusters.Clusters);
            }
            catch (RasRemoteException ex)
            {
                // RAS без ragent: ожидаем осмысленное декодированное исключение (ragent connection refused)
                Assert.Contains("cluster", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.IsType<RasRemoteException>(ex.InnerException);
            }
        }
    }

    [SkippableFact]
    public async Task AuthenticateAgent_Without_Ragent_Fails_Cleanly()
    {
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (connection, endpoint) = await OpenAdminEndpointAsync(cts.Token);
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
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var (connection, endpoint) = await OpenAdminEndpointAsync(cts.Token);
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
        Skip.IfNot(ServerReachable(), $"RAS недоступен на {Host}:{Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var (connection, endpoint) = await OpenAdminEndpointAsync(cts.Token);
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

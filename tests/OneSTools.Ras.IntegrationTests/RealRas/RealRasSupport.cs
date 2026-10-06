using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Transport;

namespace OneSTools.Ras.IntegrationTests.RealRas;

/// <summary>
/// Общие утилы тестов против реального RAS. Высокоуровневый путь клиента покрыт
/// RasAdminClientAgainstRealRasTests; здесь низкоуровневый доступ (RasConnection +
/// ручной negotiation версий) для проверки транспорта и форматов сообщений.
/// </summary>
internal static class RealRasSupport
{
    public const string AdminServiceName = "v8.service.Admin.Cluster";
    public static readonly string[] CandidateVersions = { "14.0", "13.0", "12.0", "11.0", "10.0", "9.0", "8.0", "7.0", "6.0", "5.0", "4.0", "3.0" };

    public static string Host { get; } = Environment.GetEnvironmentVariable("RAC_TEST_RAS_HOST") ?? "1c-test-server";
    public static int Port { get; } = int.Parse(Environment.GetEnvironmentVariable("RAC_TEST_RAS_PORT") ?? "1545");

    public static bool ServerReachable()
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

    /// <summary>EndPoint сервера: IP как есть, имя — DnsEndPoint (поддержан RasConnection).</summary>
    public static EndPoint ResolveServer() =>
        IPAddress.TryParse(Host, out var literal)
            ? new IPEndPoint(literal, Port)
            : new DnsEndPoint(Host, Port);

    public static async Task<(RasConnection Connection, RasEndpoint Endpoint)> OpenAdminEndpointAsync(CancellationToken cancellationToken)
    {
        var connection = new RasConnection(ResolveServer(), NullLogger<RasConnection>.Instance);
        await connection.ConnectAsync(new Dictionary<string, object?> { { "client.name", "onestools_ras_integration_test" } }, cancellationToken);

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

    public static int ProtoVersion(this RasEndpoint endpoint) => int.Parse(endpoint.ServiceId.Version.Split('.')[0]);
}

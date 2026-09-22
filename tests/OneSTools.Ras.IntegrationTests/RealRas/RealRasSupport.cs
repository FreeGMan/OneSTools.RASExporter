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
/// Общие утилы тестов против реального RAS. Обходят зафиксированные баги клиента:
/// #1 DnsEndPoint (TODO.md) — резолвим IPv4 сами; #2/#3 неверный ServiceId и
/// отсутствие negotiation версий — открываем эндпоинт напрямую RasConnection.
/// </summary>
internal static class RealRasSupport
{
    public const string AdminServiceName = "v8.service.Admin.Cluster";
    public static readonly string[] CandidateVersions = { "12.0", "11.0", "10.0", "9.0", "8.0", "7.0", "6.0", "5.0", "4.0", "3.0" };

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

    public static IPEndPoint ResolveServer()
    {
        if (IPAddress.TryParse(Host, out var literal))
            return new IPEndPoint(literal, Port);
        var ipv4 = Dns.GetHostAddresses(Host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        return new IPEndPoint(ipv4, Port);
    }

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

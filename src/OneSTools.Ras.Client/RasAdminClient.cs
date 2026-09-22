using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Transport;

namespace OneSTools.Ras.Client;

/// <summary>
/// Высокоуровневый клиент для взаимодействия с сервером администрирования 1С (RAS)
/// по протоколу SWP через виртуальный канал IAgentAdminConnection.
/// </summary>
public sealed class RasAdminClient : IAsyncDisposable
{
    /// <summary>
    /// Wire-имя сервиса администрирования RAS (Java SDK: AgentAdminConnectionProxy.SERVICE_NAME).
    /// FQN интерфейса IAgentAdminConnection — это имя Java-интерфейса, а не имя сервиса в протоколе.
    /// Источник: reference/decompiled/java-src/.../AgentAdminConnectionProxy.java:174-175
    /// и подтверждено реальным сервером 8.3.25.1520 (поддерживает 3.0..14.0).
    /// </summary>
    public const string AdminServiceName = "v8.service.Admin.Cluster";

    /// <summary>Клиентские версии сервиса от новой к старой (перебираются при negotiation).</summary>
    public static readonly IReadOnlyList<string> AdminServiceVersions =
        ["14.0", "13.0", "12.0", "11.0", "10.0", "9.0", "8.0", "7.0", "6.0", "5.0", "4.0", "3.0"];

    private readonly RasConnection _connection;
    private RasEndpoint? _endpoint;
    private readonly ILogger<RasAdminClient> _logger;

    public RasConnection Connection => _connection;
    public RasEndpoint? Endpoint => _endpoint;

    /// <summary>Мажорная версия сервиса, согласованная при открытии эндпоинта (для версионирования форматов сообщений).</summary>
    public int ProtocolVersion { get; private set; }

    public RasAdminClient(string host, int port = 1545, ILogger<RasAdminClient>? logger = null)
        : this(CreateEndPoint(host, port), logger)
    {
    }

    public RasAdminClient(EndPoint endPoint, ILogger<RasAdminClient>? logger = null)
    {
        _logger = logger ?? NullLogger<RasAdminClient>.Instance;
        _connection = new RasConnection(endPoint);
    }

    public async Task ConnectAsync(
        string? agentUser = null,
        string? agentPassword = null,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Подключение к RAS-серверу...");
        await _connection.ConnectAsync(parameters, cancellationToken);

        _logger.LogInformation("Открытие эндпоинта администрирования ({Service})...", AdminServiceName);
        _endpoint = await OpenAdminEndpointWithNegotiationAsync(cancellationToken);

        if (!string.IsNullOrEmpty(agentUser))
        {
            _logger.LogInformation("Аутентификация агента: {User}", agentUser);
            await AuthenticateAgentAsync(agentUser, agentPassword ?? string.Empty, cancellationToken);
        }

        _logger.LogInformation("RAS-клиент готов к работе (протокол v{Version})", ProtocolVersion);
    }

    /// <summary>
    /// Перебор версий от новой к старой с ограничением множества по списку supported=
    /// из ответа UnsupportedService (аналог V8Connection.startService/detectSupportedVersions в Java SDK).
    /// </summary>
    private async Task<RasEndpoint> OpenAdminEndpointWithNegotiationAsync(CancellationToken cancellationToken)
    {
        HashSet<string>? serverSupported = null;
        Exception? lastFailure = null;

        foreach (var version in AdminServiceVersions)
        {
            if (serverSupported is not null && !serverSupported.Contains(version))
                continue;

            try
            {
                var endpoint = await _connection.OpenEndpointAsync(new ServiceId(AdminServiceName, version), cancellationToken: cancellationToken);
                ProtocolVersion = ParseMajorVersion(version);
                _logger.LogInformation("Эндпоинт администрирования открыт: версия {Version}", version);
                return endpoint;
            }
            catch (RasRemoteException ex) when (IsUnsupportedService(ex))
            {
                lastFailure = ex;
                var detected = DetectSupportedVersions(ex);
                if (detected.Count > 0)
                    serverSupported = detected;
                _logger.LogDebug("Версия {Version} не поддерживается сервером, продолжаем negotiation", version);
            }
        }

        throw lastFailure ?? new RasException(
            $"Ни одна из версий сервиса {AdminServiceName} не поддерживается сервером" +
            (serverSupported is not null ? $"; сервер поддерживает: {string.Join(",", serverSupported)}" : string.Empty));
    }

    private static int ParseMajorVersion(string version)
        => int.TryParse(version.Split('.')[0], out var major) ? major : 0;

    private static bool IsUnsupportedService(RasRemoteException exception)
    {
        for (Exception? current = exception; current is RasRemoteException remote; current = remote.InnerException)
        {
            if (remote.RemoteClassName.Contains("UnsupportedService", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static HashSet<string> DetectSupportedVersions(RasRemoteException exception)
    {
        var versions = new HashSet<string>(StringComparer.Ordinal);
        for (Exception? current = exception; current is RasRemoteException remote; current = remote.InnerException)
        {
            int start = remote.Message.IndexOf(" supported=", StringComparison.Ordinal);
            if (start < 0)
                continue;

            start += " supported=".Length;
            int end = remote.Message.IndexOf(']', start);
            string list = end >= 0 ? remote.Message[start..end] : remote.Message[start..];
            foreach (var v in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                versions.Add(v);
            }
        }
        return versions;
    }

    private RasEndpoint EnsureEndpoint()
    {
        return _endpoint ?? throw new InvalidOperationException("Клиент не подключен. Вызовите ConnectAsync() перед выполнением операций.");
    }

    public async Task<IReadOnlyList<ClusterInfo>> GetClustersAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClustersRequest(), ProtocolVersion, cancellationToken);
        return response.Clusters;
    }

    public async Task<ClusterInfo> GetClusterInfoAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterInfoRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionsRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Sessions;
    }

    public async Task<IReadOnlyList<SessionInfo>> GetInfoBaseSessionsAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseSessionsRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Sessions;
    }

    public async Task<SessionInfo> GetSessionInfoAsync(Guid clusterId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionInfoRequest(clusterId, sessionId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    public Task TerminateSessionAsync(Guid clusterId, Guid sessionId, string? message = null, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new TerminateSessionRequest(clusterId, sessionId, message), ProtocolVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessInfo>> GetWorkingProcessesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingProcessesRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Processes;
    }

    public async Task<ProcessInfo> GetWorkingProcessInfoAsync(Guid clusterId, Guid processId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingProcessInfoRequest(clusterId, processId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    public async Task<IReadOnlyList<ConnectionInfo>> GetConnectionsShortAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetConnectionsShortRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Connections;
    }

    public async Task<IReadOnlyList<ConnectionInfo>> GetInfoBaseConnectionsShortAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseConnectionsShortRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Connections;
    }

    public Task DisconnectAsync(Guid clusterId, Guid processId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new DisconnectRequest(clusterId, processId, connectionId), ProtocolVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<LockInfo>> GetLocksAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetLocksRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<LockInfo>> GetInfoBaseLocksAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseLocksRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<LockInfo>> GetConnectionLocksAsync(Guid clusterId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetConnectionLocksRequest(clusterId, connectionId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<LockInfo>> GetSessionLocksAsync(Guid clusterId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionLocksRequest(clusterId, sessionId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<InfobaseSummaryInfo>> GetInfoBasesShortAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBasesShortRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.InfoBases;
    }

    public async Task<IReadOnlyList<InfobaseInfo>> GetInfoBasesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBasesRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.InfoBases;
    }

    public async Task<InfobaseInfo> GetInfoBaseInfoAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseInfoRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    public async Task<IReadOnlyList<ServerInfo>> GetWorkingServersAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingServersRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Servers;
    }

    public async Task<IReadOnlyList<ManagerInfo>> GetClusterManagersAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterManagersRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Managers;
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetClusterServicesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterServicesRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Services;
    }

    public Task AuthenticateAgentAsync(string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AuthenticateAgentRequest(user, password), ProtocolVersion, cancellationToken);
    }

    public Task AuthenticateClusterAsync(Guid clusterId, string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AuthenticateRequest(clusterId, user, password), ProtocolVersion, cancellationToken);
    }

    public Task AddAuthenticationAsync(Guid infobaseId, string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AddAuthenticationRequest(infobaseId, user, password), ProtocolVersion, cancellationToken);
    }

    public async Task<string> GetAgentVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetAgentVersionRequest(), ProtocolVersion, cancellationToken);
        return response.Version;
    }

    public async Task<IReadOnlyList<AdminInfo>> GetAgentAdminsAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetAgentAdminsRequest(), ProtocolVersion, cancellationToken);
        return response.Admins;
    }

    public async Task<IReadOnlyList<AdminInfo>> GetClusterAdminsAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterAdminsRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Admins;
    }

    private static EndPoint CreateEndPoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            return new IPEndPoint(ip, port);
        }
        return new DnsEndPoint(host, port);
    }

    public async ValueTask DisposeAsync()
    {
        if (_endpoint is not null)
        {
            try { await _endpoint.DisposeAsync(); } catch { }
        }
        await _connection.DisposeAsync();
    }
}

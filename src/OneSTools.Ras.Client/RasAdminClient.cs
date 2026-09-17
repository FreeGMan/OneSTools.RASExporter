using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private static readonly ServiceId AdminServiceId = new("com.1c.v8.ibis.admin.IAgentAdminConnection", "1.0");

    private readonly RasConnection _connection;
    private RasEndpoint? _endpoint;
    private readonly ILogger<RasAdminClient> _logger;

    public RasConnection Connection => _connection;
    public RasEndpoint? Endpoint => _endpoint;

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

        _logger.LogInformation("Открытие эндпоинта администрирования ({Service})...", AdminServiceId);
        _endpoint = await _connection.OpenEndpointAsync(AdminServiceId, cancellationToken: cancellationToken);

        if (!string.IsNullOrEmpty(agentUser))
        {
            _logger.LogInformation("Аутентификация агента: {User}", agentUser);
            await AuthenticateAgentAsync(agentUser, agentPassword ?? string.Empty, cancellationToken);
        }

        _logger.LogInformation("RAS-клиент готов к работе");
    }

    private RasEndpoint EnsureEndpoint()
    {
        return _endpoint ?? throw new InvalidOperationException("Клиент не подключен. Вызовите ConnectAsync() перед выполнением операций.");
    }

    public async Task<IReadOnlyList<ClusterInfo>> GetClustersAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClustersRequest(), cancellationToken: cancellationToken);
        return response.Clusters;
    }

    public async Task<ClusterInfo> GetClusterInfoAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterInfoRequest(clusterId), cancellationToken: cancellationToken);
        return response.Info;
    }

    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionsRequest(clusterId), cancellationToken: cancellationToken);
        return response.Sessions;
    }

    public async Task<IReadOnlyList<SessionInfo>> GetInfoBaseSessionsAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseSessionsRequest(clusterId, infobaseId), cancellationToken: cancellationToken);
        return response.Sessions;
    }

    public async Task<SessionInfo> GetSessionInfoAsync(Guid clusterId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionInfoRequest(clusterId, sessionId), cancellationToken: cancellationToken);
        return response.Info;
    }

    public Task TerminateSessionAsync(Guid clusterId, Guid sessionId, string? message = null, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new TerminateSessionRequest(clusterId, sessionId, message), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessInfo>> GetWorkingProcessesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingProcessesRequest(clusterId), cancellationToken: cancellationToken);
        return response.Processes;
    }

    public async Task<ProcessInfo> GetWorkingProcessInfoAsync(Guid clusterId, Guid processId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingProcessInfoRequest(clusterId, processId), cancellationToken: cancellationToken);
        return response.Info;
    }

    public async Task<IReadOnlyList<ConnectionInfo>> GetConnectionsShortAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetConnectionsShortRequest(clusterId), cancellationToken: cancellationToken);
        return response.Connections;
    }

    public async Task<IReadOnlyList<ConnectionInfo>> GetInfoBaseConnectionsShortAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseConnectionsShortRequest(clusterId, infobaseId), cancellationToken: cancellationToken);
        return response.Connections;
    }

    public Task DisconnectAsync(Guid clusterId, Guid processId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new DisconnectRequest(clusterId, processId, connectionId), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<LockInfo>> GetLocksAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetLocksRequest(clusterId), cancellationToken: cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<LockInfo>> GetInfoBaseLocksAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseLocksRequest(clusterId, infobaseId), cancellationToken: cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<LockInfo>> GetConnectionLocksAsync(Guid clusterId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetConnectionLocksRequest(clusterId, connectionId), cancellationToken: cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<LockInfo>> GetSessionLocksAsync(Guid clusterId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionLocksRequest(clusterId, sessionId), cancellationToken: cancellationToken);
        return response.Locks;
    }

    public async Task<IReadOnlyList<InfobaseSummaryInfo>> GetInfoBasesShortAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBasesShortRequest(clusterId), cancellationToken: cancellationToken);
        return response.InfoBases;
    }

    public async Task<IReadOnlyList<InfobaseInfo>> GetInfoBasesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBasesRequest(clusterId), cancellationToken: cancellationToken);
        return response.InfoBases;
    }

    public async Task<InfobaseInfo> GetInfoBaseInfoAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseInfoRequest(clusterId, infobaseId), cancellationToken: cancellationToken);
        return response.Info;
    }

    public async Task<IReadOnlyList<ServerInfo>> GetWorkingServersAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingServersRequest(clusterId), cancellationToken: cancellationToken);
        return response.Servers;
    }

    public async Task<IReadOnlyList<ManagerInfo>> GetClusterManagersAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterManagersRequest(clusterId), cancellationToken: cancellationToken);
        return response.Managers;
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetClusterServicesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterServicesRequest(clusterId), cancellationToken: cancellationToken);
        return response.Services;
    }

    public Task AuthenticateAgentAsync(string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AuthenticateAgentRequest(user, password), cancellationToken: cancellationToken);
    }

    public Task AuthenticateClusterAsync(Guid clusterId, string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AuthenticateRequest(clusterId, user, password), cancellationToken: cancellationToken);
    }

    public Task AddAuthenticationAsync(Guid infobaseId, string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AddAuthenticationRequest(infobaseId, user, password), cancellationToken: cancellationToken);
    }

    public async Task<string> GetAgentVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetAgentVersionRequest(), cancellationToken: cancellationToken);
        return response.Version;
    }

    public async Task<IReadOnlyList<AdminInfo>> GetAgentAdminsAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetAgentAdminsRequest(), cancellationToken: cancellationToken);
        return response.Admins;
    }

    public async Task<IReadOnlyList<AdminInfo>> GetClusterAdminsAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterAdminsRequest(clusterId), cancellationToken: cancellationToken);
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

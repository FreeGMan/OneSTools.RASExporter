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
    /// Wire-имя сервиса администрирования RAS (Java SDK: AgentAdminConnectionProxy.SERVICE_NAME,
    /// пакет com._1c.v8.ibis.internal.admin официальной Java-библиотеки 1С).
    /// FQN интерфейса IAgentAdminConnection — это имя Java-интерфейса, а не имя сервиса в протоколе.
    /// Подтверждено реальным сервером 8.3.25.1520 (docs/TRANSPORT_SPEC.md §7.2): поддерживает 3.0..14.0.
    /// </summary>
    public const string AdminServiceName = "v8.service.Admin.Cluster";

    /// <summary>Клиентские версии сервиса от новой к старой (перебираются при negotiation).</summary>
    /// <summary>Кандидатские версии сервиса администрирования от новой к старой (перебираются при negotiation).</summary>
    public static readonly IReadOnlyList<string> AdminServiceVersions =
        ["14.0", "13.0", "12.0", "11.0", "10.0", "9.0", "8.0", "7.0", "6.0", "5.0", "4.0", "3.0"];

    private readonly RasConnection _connection;
    private RasEndpoint? _endpoint;
    private readonly ILogger<RasAdminClient> _logger;

    /// <summary>Нижелевяное SWP-соединение.</summary>
    public RasConnection Connection => _connection;
    /// <summary>Открытый эндпоинт сервиса администрирования (null до <see cref="ConnectAsync"/>).</summary>
    public RasEndpoint? Endpoint => _endpoint;

    /// <summary>Мажорная версия сервиса, согласованная при открытии эндпоинта (для версионирования форматов сообщений).</summary>
    /// <summary>Мажорная согласованная версия сервиса; передаётся версией формата во все вызовы API.</summary>
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

    /// <summary>TCP + Negotiate/Connect + negotiation эндпоинта <see cref="AdminServiceName"/> + (опционально) authenticateAgent.</summary>
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

    /// <summary>Список кластеров, зарегистрированных в RAS (без аутентификации).</summary>
    public async Task<IReadOnlyList<ClusterInfo>> GetClustersAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClustersRequest(), ProtocolVersion, cancellationToken);
        return response.Clusters;
    }

    /// <summary>Конфигурация кластера.</summary>
    public async Task<ClusterInfo> GetClusterInfoAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterInfoRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    /// <summary>Активные сессии кластера (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionsRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Sessions;
    }

    /// <summary>Активные сессии указанной информационной базы (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<SessionInfo>> GetInfoBaseSessionsAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseSessionsRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Sessions;
    }

    /// <summary>Данные одной сессии.</summary>
    public async Task<SessionInfo> GetSessionInfoAsync(Guid clusterId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionInfoRequest(clusterId, sessionId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    /// <summary>Принудительное завершение сессии с сообщением.</summary>
    public Task TerminateSessionAsync(Guid clusterId, Guid sessionId, string? message = null, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new TerminateSessionRequest(clusterId, sessionId, message), ProtocolVersion, cancellationToken);
    }

    /// <summary>Рабочие процессы кластера (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<ProcessInfo>> GetWorkingProcessesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingProcessesRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Processes;
    }

    /// <summary>Данные одного рабочего процесса.</summary>
    public async Task<ProcessInfo> GetWorkingProcessInfoAsync(Guid clusterId, Guid processId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingProcessInfoRequest(clusterId, processId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    /// <summary>Краткие сведения о соединениях кластера (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<ConnectionInfo>> GetConnectionsShortAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetConnectionsShortRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Connections;
    }

    /// <summary>Краткие сведения о соединениях информационной базы.</summary>
    public async Task<IReadOnlyList<ConnectionInfo>> GetInfoBaseConnectionsShortAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseConnectionsShortRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Connections;
    }

    /// <summary>Принудительное отсоединение соединения от процесса.</summary>
    public Task DisconnectAsync(Guid clusterId, Guid processId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new DisconnectRequest(clusterId, processId, connectionId), ProtocolVersion, cancellationToken);
    }

    /// <summary>Текущие блокировки кластера (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<LockInfo>> GetLocksAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetLocksRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    /// <summary>Блокировки информационной базы.</summary>
    public async Task<IReadOnlyList<LockInfo>> GetInfoBaseLocksAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseLocksRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    /// <summary>Блокировки, удерживаемые соединением.</summary>
    public async Task<IReadOnlyList<LockInfo>> GetConnectionLocksAsync(Guid clusterId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetConnectionLocksRequest(clusterId, connectionId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    /// <summary>Блокировки, удерживаемые сессией.</summary>
    public async Task<IReadOnlyList<LockInfo>> GetSessionLocksAsync(Guid clusterId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetSessionLocksRequest(clusterId, sessionId), ProtocolVersion, cancellationToken);
        return response.Locks;
    }

    /// <summary>Краткие сведения об информационных базах кластера.</summary>
    public async Task<IReadOnlyList<InfobaseSummaryInfo>> GetInfoBasesShortAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBasesShortRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.InfoBases;
    }

    /// <summary>Полные конфигурации информационных баз кластера.</summary>
    public async Task<IReadOnlyList<InfobaseInfo>> GetInfoBasesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBasesRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.InfoBases;
    }

    /// <summary>Конфигурация одной информационной базы.</summary>
    public async Task<InfobaseInfo> GetInfoBaseInfoAsync(Guid clusterId, Guid infobaseId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetInfoBaseInfoRequest(clusterId, infobaseId), ProtocolVersion, cancellationToken);
        return response.Info;
    }

    /// <summary>Рабочие серверы 1C кластера (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<ServerInfo>> GetWorkingServersAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetWorkingServersRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Servers;
    }

    /// <summary>Менеджеры кластера (требует authenticateCluster).</summary>
    public async Task<IReadOnlyList<ManagerInfo>> GetClusterManagersAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterManagersRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Managers;
    }

    /// <summary>Сервисы кластера.</summary>
    public async Task<IReadOnlyList<ServiceInfo>> GetClusterServicesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetClusterServicesRequest(clusterId), ProtocolVersion, cancellationToken);
        return response.Services;
    }

    /// <summary>Вход администратора агента (сервера RAS).</summary>
    public Task AuthenticateAgentAsync(string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AuthenticateAgentRequest(user, password), ProtocolVersion, cancellationToken);
    }

    /// <summary>Вход администратора кластера. Обязателен перед методами просмотра (сессии/процессы/соединения/блокировки), пустые учётки допустимы для кластеров без аутентификации (docs/SECURITY.md §2, TRANSPORT_SPEC §7.2).</summary>
    public Task AuthenticateClusterAsync(Guid clusterId, string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AuthenticateRequest(clusterId, user, password), ProtocolVersion, cancellationToken);
    }

    /// <summary>Добавление администратора информационной базы.</summary>
    public Task AddAuthenticationAsync(Guid infobaseId, string user, string password, CancellationToken cancellationToken = default)
    {
        return EnsureEndpoint().ExecuteVoidAsync(new AddAuthenticationRequest(infobaseId, user, password), ProtocolVersion, cancellationToken);
    }

    /// <summary>Версия платформы агента (например «8.3.25.1520»), без аутентификации.</summary>
    public async Task<string> GetAgentVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetAgentVersionRequest(), ProtocolVersion, cancellationToken);
        return response.Version;
    }

    /// <summary>Список администраторов агента (требует authenticateAgent).</summary>
    public async Task<IReadOnlyList<AdminInfo>> GetAgentAdminsAsync(CancellationToken cancellationToken = default)
    {
        var response = await EnsureEndpoint().ExecuteAsync(new GetAgentAdminsRequest(), ProtocolVersion, cancellationToken);
        return response.Admins;
    }

    /// <summary>Список администраторов кластера (требует authenticateCluster).</summary>
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

    /// <summary>Закрывает эндпоинт и соединение.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_endpoint is not null)
        {
            try { await _endpoint.DisposeAsync(); } catch { }
        }
        await _connection.DisposeAsync();
    }
}

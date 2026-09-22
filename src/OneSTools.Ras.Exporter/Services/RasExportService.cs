using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Exporter.Configuration;
using OneSTools.Ras.Exporter.Core.Collectors;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Services;

public sealed class RasExportService : BackgroundService
{
    private readonly RasExporterSettings _rasSettings;
    private readonly ClickHouseSettings _clickHouseSettings;
    private readonly IMetricsStorage _storage;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RasExportService> _logger;

    public RasExportService(
        IOptions<RasExporterSettings> rasSettings,
        IOptions<ClickHouseSettings> clickHouseSettings,
        IMetricsStorage storage,
        MetricsBatchingPipeline pipeline,
        ILoggerFactory loggerFactory,
        ILogger<RasExportService> logger)
    {
        _rasSettings = rasSettings.Value;
        _clickHouseSettings = clickHouseSettings.Value;
        _storage = storage;
        _pipeline = pipeline;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запуск службы OneSTools.Ras.Exporter...");

        _pipeline.Start();

        if (_rasSettings.Servers.Count == 0)
        {
            _logger.LogWarning("В конфигурации RasExporter:Servers не указано ни одного сервера RAS");
        }

        var serverTasks = _rasSettings.Servers
            .Select(server => RunServerLoopAsync(server, stoppingToken))
            .ToArray();

        try
        {
            await Task.WhenAll(serverTasks);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            _logger.LogInformation("Остановка службы OneSTools.Ras.Exporter, сброс буферов...");
            await _pipeline.DisposeAsync();
            _logger.LogInformation("Служба OneSTools.Ras.Exporter успешно остановлена");
        }
    }

    private async Task RunServerLoopAsync(RasServerSettings server, CancellationToken stoppingToken)
    {
        var serverLogger = _loggerFactory.CreateLogger($"RasServer.{server.Name}");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                serverLogger.LogInformation("Подключение к серверу RAS {Name} ({Host}:{Port})...", server.Name, server.Host, server.Port);

                var clientLogger = _loggerFactory.CreateLogger<RasAdminClient>();
                await using var client = new RasAdminClient(server.Host, server.Port, clientLogger);

                var connectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(server.ConnectTimeoutMilliseconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, connectCts.Token);

                await client.ConnectAsync(server.AgentAuth.User, server.AgentAuth.Password, cancellationToken: linkedCts.Token);
                serverLogger.LogInformation("Успешное подключение к серверу RAS {Name}", server.Name);

                // Discover clusters
                var discoveredClusters = await client.GetClustersAsync(stoppingToken);
                serverLogger.LogInformation("Обнаружено {Count} кластеров на сервере {Name}", discoveredClusters.Count, server.Name);

                var clusterWorkers = new List<Task>();
                var intervals = new ClusterCollectorIntervals(
                    server.PollIntervals.Clusters,
                    server.PollIntervals.Sessions,
                    server.PollIntervals.Processes,
                    server.PollIntervals.Connections,
                    server.PollIntervals.Locks);

                var targetClusters = ResolveTargetClusters(server, discoveredClusters);

                foreach (var (clusterId, clusterName, user, password) in targetClusters)
                {
                    // Реальный RAS требует явный authenticateCluster даже для кластеров
                    // без аутентификации (пустые учётки = анонимный вход). Без него
                    // GetSessions/GetWorkingProcesses/... отваливаются с #Rights (см. TODO.md п.8).
                    try
                    {
                        await client.AuthenticateClusterAsync(clusterId, user ?? string.Empty, password ?? string.Empty, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        serverLogger.LogError(ex, "Ошибка аутентификации в кластере {ClusterName} ({ClusterId})", clusterName, clusterId);
                        continue;
                    }

                    var worker = new ClusterMetricsWorker(
                        client, clusterId, clusterName, _pipeline, _storage, intervals,
                        _loggerFactory.CreateLogger($"Cluster.{clusterName}"));

                    clusterWorkers.Add(worker.RunAsync(stoppingToken));
                }

                if (clusterWorkers.Count > 0)
                {
                    await Task.WhenAll(clusterWorkers);
                }
                else
                {
                    serverLogger.LogWarning("Нет активных кластеров для сбора на сервере {Name}", server.Name);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                serverLogger.LogError(ex, "Ошибка соединения с сервером RAS {Name}. Повтор через 5 секунд...", server.Name);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static List<(Guid ClusterId, string ClusterName, string? User, string? Password)> ResolveTargetClusters(
        RasServerSettings server,
        IReadOnlyList<ClusterInfo> discoveredClusters)
    {
        var result = new List<(Guid ClusterId, string ClusterName, string? User, string? Password)>();

        if (server.Clusters.Count > 0)
        {
            foreach (var clusterCfg in server.Clusters.Where(c => c.Enabled))
            {
                Guid clusterId = clusterCfg.ClusterId ?? Guid.Empty;
                string clusterName = clusterCfg.ClusterName;

                if (clusterId == Guid.Empty)
                {
                    var match = discoveredClusters.FirstOrDefault(c =>
                        c.Name.Equals(clusterName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        clusterId = match.Uuid;
                    }
                }
                else if (string.IsNullOrEmpty(clusterName))
                {
                    var match = discoveredClusters.FirstOrDefault(c => c.Uuid == clusterId);
                    if (match != null)
                    {
                        clusterName = match.Name;
                    }
                }

                if (clusterId != Guid.Empty)
                {
                    result.Add((clusterId, string.IsNullOrEmpty(clusterName) ? clusterId.ToString() : clusterName, clusterCfg.User, clusterCfg.Password));
                }
            }
        }
        else
        {
            // Auto-target all discovered clusters
            foreach (var c in discoveredClusters)
            {
                result.Add((c.Uuid, c.Name, null, null));
            }
        }

        return result;
    }
}

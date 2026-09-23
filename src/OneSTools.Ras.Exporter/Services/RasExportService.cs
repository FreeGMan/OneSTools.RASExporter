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
using OneSTools.Ras.Exporter.Services.Health;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Services;

/// <summary>
/// Диспетчер сбора: вариант C — на каждую пару (сервер RAS, кластер) своё SWP-соединение
/// со своим reconnect-циклом; обнаружение кластеров — отдельный периодический проход.
/// Изоляция: падение/тормоза одного кластера не влияют на остальные (замеры LoadSim).
/// </summary>
public sealed class RasExportService : BackgroundService
{
    private readonly RasExporterSettings _rasSettings;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly IMetricsStorage _storage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RasExportService> _logger;
    private readonly RasExporterHealthRegistry _health;
    private readonly StorageHealthGate _gate;

    public RasExportService(
        IOptions<RasExporterSettings> rasSettings,
        IOptions<ClickHouseSettings> clickHouseSettings,
        IMetricsStorage storage,
        MetricsBatchingPipeline pipeline,
        ILoggerFactory loggerFactory,
        ILogger<RasExportService> logger,
        RasExporterHealthRegistry health,
        StorageHealthGate gate)
    {
        _rasSettings = rasSettings.Value;
        _storage = storage;
        _pipeline = pipeline;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _health = health;
        _gate = gate;
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
            .Select(server => RunServerAsync(server, stoppingToken))
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

    private async Task RunServerAsync(RasServerSettings server, CancellationToken stoppingToken)
    {
        var serverLogger = _loggerFactory.CreateLogger($"RasServer.{server.Name}");
        var intervals = new ClusterCollectorIntervals(
            server.PollIntervals.Clusters,
            server.PollIntervals.Sessions,
            server.PollIntervals.Processes,
            server.PollIntervals.Connections,
            server.PollIntervals.Locks);

        var running = new Dictionary<(Guid, string), CancellationTokenSource>();
        var allCts = new List<CancellationTokenSource>();
        var clusterTasks = new List<Task>();
        var discoveryBackoff = new BackoffPolicy();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<ClusterTarget> targets;
                try
                {
                    targets = await DiscoverTargetsAsync(server, stoppingToken);
                    discoveryBackoff.Reset();
                    serverLogger.LogInformation("Обнаружено {Count} целевых кластеров на сервере {Name}", targets.Count, server.Name);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TimeSpan retryIn = discoveryBackoff.NextDelay();
                    serverLogger.LogError(ex, "Ошибка обнаружения кластеров на {Name}. Повтор через {Delay:F1} c...", server.Name, retryIn.TotalSeconds);
                    try { await Task.Delay(retryIn, stoppingToken); } catch (OperationCanceledException) { break; }
                    continue;
                }

                foreach (var target in targets)
                {
                    var key = (target.ClusterId, target.User ?? string.Empty);
                    if (running.ContainsKey(key))
                        continue;

                    var clusterCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    running[key] = clusterCts;
                    allCts.Add(clusterCts);
                    clusterTasks.Add(RunClusterLoopAsync(server, target, intervals, clusterCts));
                    serverLogger.LogInformation("Кластер {ClusterName} ({ClusterId}): запущен выделенный конвейер сбора", target.ClusterName, target.ClusterId);
                }

                var wanted = targets.Select(t => (t.ClusterId, t.User ?? string.Empty)).ToHashSet();
                foreach (var (key, cts) in running.ToList())
                {
                    if (wanted.Contains(key))
                        continue;

                    cts.Cancel();
                    running.Remove(key);
                    serverLogger.LogInformation("Кластер {ClusterId}: убран из конфигурации/списка, сбор остановлен", key.Item1);
                }

                try
                {
                    await Task.Delay(server.PollIntervals.Clusters, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            foreach (var cts in running.Values)
                cts.Cancel();

            await Task.WhenAll(clusterTasks.Select(t => t.ContinueWith(static t => t.Exception, TaskScheduler.Default)));
            foreach (var cts in allCts)
                cts.Dispose();
        }
    }

    private async Task<List<ClusterTarget>> DiscoverTargetsAsync(RasServerSettings server, CancellationToken stoppingToken)
    {
        await using var client = new RasAdminClient(server.Host, server.Port, _loggerFactory.CreateLogger<RasAdminClient>());

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        connectCts.CancelAfter(TimeSpan.FromMilliseconds(server.ConnectTimeoutMilliseconds));

        await client.ConnectAsync(server.AgentAuth.User, server.AgentAuth.Password, cancellationToken: connectCts.Token);
        var discovered = await client.GetClustersAsync(connectCts.Token);

        return ResolveTargetClusters(server, discovered);
    }

    private async Task RunClusterLoopAsync(RasServerSettings server, ClusterTarget target, ClusterCollectorIntervals intervals, CancellationTokenSource clusterCts)
    {
        var token = clusterCts.Token;
        var clusterLogger = _loggerFactory.CreateLogger($"Cluster.{target.ClusterName}");
        var backoff = new BackoffPolicy();
        string healthKey = $"{server.Name}/{target.ClusterName}";

        while (!token.IsCancellationRequested)
        {
            RasAdminClient? client = null;
            try
            {
                client = new RasAdminClient(server.Host, server.Port, _loggerFactory.CreateLogger<RasAdminClient>());

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(TimeSpan.FromMilliseconds(server.ConnectTimeoutMilliseconds));

                clusterLogger.LogInformation("Подключение к RAS {Host}:{Port} для кластера...", server.Host, server.Port);
                await client.ConnectAsync(server.AgentAuth.User, server.AgentAuth.Password, cancellationToken: connectCts.Token);

                // Реальный RAS требует явный authenticateCluster даже с пустыми учётками.
                await client.AuthenticateClusterAsync(target.ClusterId, target.User ?? string.Empty, target.Password ?? string.Empty, token);
                backoff.Reset();
                _health.MarkConnected(healthKey);
                clusterLogger.LogInformation("Сессия RAS для кластера {ClusterName} установлена", target.ClusterName);

                var worker = new ClusterMetricsWorker(
                    client, target.ClusterId, target.ClusterName, _pipeline, _storage, intervals, clusterLogger, _gate);

                await worker.RunAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _health.MarkFailed(healthKey, ex.Message);
                TimeSpan retryIn = backoff.NextDelay();
                clusterLogger.LogError(ex, "Ошибка сессии RAS кластера {ClusterName}. Переподключение через {Delay:F1} c...", target.ClusterName, retryIn.TotalSeconds);
                try { await Task.Delay(retryIn, token); } catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (client is not null)
                {
                    await client.DisposeAsync();
                }
            }
        }

        clusterLogger.LogInformation("Сбор метрик кластера {ClusterName} остановлен", target.ClusterName);
    }

    private static List<ClusterTarget> ResolveTargetClusters(
        RasServerSettings server,
        IReadOnlyList<ClusterInfo> discoveredClusters)
    {
        var result = new List<ClusterTarget>();

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
                    result.Add(new ClusterTarget(
                        clusterId,
                        string.IsNullOrEmpty(clusterName) ? clusterId.ToString() : clusterName,
                        clusterCfg.User,
                        clusterCfg.Password));
                }
            }
        }
        else
        {
            // Auto-target all discovered clusters
            foreach (var c in discoveredClusters)
            {
                result.Add(new ClusterTarget(c.Uuid, c.Name, null, null));
            }
        }

        return result;
    }

    private sealed record ClusterTarget(Guid ClusterId, string ClusterName, string? User, string? Password);
}

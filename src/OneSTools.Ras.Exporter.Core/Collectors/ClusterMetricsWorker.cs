using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Collectors;

/// <summary>Интервалы опроса по типам метрик.</summary>
public sealed record ClusterCollectorIntervals(
    TimeSpan Clusters,
    TimeSpan Sessions,
    TimeSpan Processes,
    TimeSpan Connections,
    TimeSpan Locks);

/// <summary>Композиция 5 коллекторов одного кластера поверх одного RasAdminClient-соединения (паттерн C, docs/SECURITY.md §2).</summary>
public sealed class ClusterMetricsWorker
{
    private readonly RasAdminClient _client;
    private readonly Guid _clusterId;
    private readonly string _clusterName;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly IMetricsStorage _storage;
    private readonly ClusterCollectorIntervals _intervals;
    private readonly ILogger _logger;
    private readonly StorageHealthGate? _gate;
    private readonly Action<string, Exception?>? _pollStatus;

    public ClusterMetricsWorker(
        RasAdminClient client,
        Guid clusterId,
        string clusterName,
        MetricsBatchingPipeline pipeline,
        IMetricsStorage storage,
        ClusterCollectorIntervals intervals,
        ILogger? logger = null,
        StorageHealthGate? gate = null,
        Action<string, Exception?>? pollStatus = null)
    {
        _gate = gate;
        _pollStatus = pollStatus;
        _client = client;
        _clusterId = clusterId;
        _clusterName = clusterName;
        _pipeline = pipeline;
        _storage = storage;
        _intervals = intervals;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Запуск сбора метрик для кластера {Name} ({Id})", _clusterName, _clusterId);

        var initializationBackoff = new BackoffPolicy();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string dbName = _storage.GetDatabaseName(_clusterName, _clusterId);
                await _storage.InitializeClusterDatabaseAsync(dbName, cancellationToken);
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка инициализации базы данных ClickHouse для кластера {Name}", _clusterName);
                if (_gate is not null && !await _storage.ProbeAsync(cancellationToken))
                {
                    _gate.MarkUnavailable();
                    await _gate.WaitUntilReadyAsync(cancellationToken);
                }
                else
                {
                    await Task.Delay(initializationBackoff.NextDelay(), cancellationToken);
                }
            }
        }

        var summaryCollector = new ClusterSummaryCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Clusters, _logger, _gate, _pollStatus);
        var sessionCollector = new SessionCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Sessions, _logger, _gate, _pollStatus);
        var processCollector = new ProcessCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Processes, _logger, _gate, _pollStatus);
        var connectionCollector = new ConnectionCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Connections, _logger, _gate, _pollStatus);
        var lockCollector = new LockCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Locks, _logger, _gate, _pollStatus);

        // Коллекторы работают параллельно (запросы сериализуются гейтом эндпоинта);
        // первая ошибка останавливает остальных — переподключение делает вызывающий слой.
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new[]
        {
            summaryCollector.RunAsync(sessionCts.Token),
            sessionCollector.RunAsync(sessionCts.Token),
            processCollector.RunAsync(sessionCts.Token),
            connectionCollector.RunAsync(sessionCts.Token),
            lockCollector.RunAsync(sessionCts.Token)
        };

        var first = await Task.WhenAny(tasks);
        sessionCts.Cancel();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch when (first.IsFaulted)
        {
        }

        if (first.IsFaulted)
            first.GetAwaiter().GetResult();
    }
}

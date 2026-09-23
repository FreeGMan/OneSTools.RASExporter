using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Collectors;

public sealed record ClusterCollectorIntervals(
    TimeSpan Clusters,
    TimeSpan Sessions,
    TimeSpan Processes,
    TimeSpan Connections,
    TimeSpan Locks);

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

    public ClusterMetricsWorker(
        RasAdminClient client,
        Guid clusterId,
        string clusterName,
        MetricsBatchingPipeline pipeline,
        IMetricsStorage storage,
        ClusterCollectorIntervals intervals,
        ILogger? logger = null,
        StorageHealthGate? gate = null)
    {
        _gate = gate;
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

        // 1. Initialize database & tables in storage
        try
        {
            string dbName = _storage.GetDatabaseName(_clusterName, _clusterId);
            await _storage.InitializeClusterDatabaseAsync(dbName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка инициализации базы данных ClickHouse для кластера {Name}", _clusterName);
        }

        // 2. Instantiate collectors
        var summaryCollector = new ClusterSummaryCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Clusters, _logger, _gate);
        var sessionCollector = new SessionCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Sessions, _logger, _gate);
        var processCollector = new ProcessCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Processes, _logger, _gate);
        var connectionCollector = new ConnectionCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Connections, _logger, _gate);
        var lockCollector = new LockCollector(_client, _clusterId, _clusterName, _pipeline, _intervals.Locks, _logger, _gate);

        // 3. Run all collectors concurrently (запросы сериализуются гейтом эндпоинта);
        // первая же ошибка (например, смерть соединения) останавливает остальных —
        // вызывающий слой делает переподключение.
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

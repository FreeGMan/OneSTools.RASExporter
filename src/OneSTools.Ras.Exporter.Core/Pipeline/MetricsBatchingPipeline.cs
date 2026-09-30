using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Pipeline;

/// <summary>
/// Главный конвейер батчинга и параллельной маршрутизации всех потоков метрик в хранилище.
/// </summary>
public sealed class MetricsBatchingPipeline : IAsyncDisposable
{
    private readonly IMetricsStorage _storage;
    private readonly ILogger<MetricsBatchingPipeline> _logger;

    private readonly BatchProcessor<ClusterSummaryMetricItem> _summaryProcessor;
    private readonly BatchProcessor<SessionMetricItem> _sessionProcessor;
    private readonly BatchProcessor<ProcessMetricItem> _processProcessor;
    private readonly BatchProcessor<ConnectionMetricItem> _connectionProcessor;
    private readonly BatchProcessor<LockMetricItem> _lockProcessor;
    private readonly BatchProcessor<InfobaseMetricItem> _infobaseProcessor;

    public MetricsBatchingPipeline(
        IMetricsStorage storage,
        int batchSize = 10000,
        TimeSpan? flushInterval = null,
        ILogger<MetricsBatchingPipeline>? logger = null,
        StorageHealthGate? gate = null)
    {
        _storage = storage;
        _logger = logger ?? NullLogger<MetricsBatchingPipeline>.Instance;

        Action<bool>? availability = gate is null
            ? null
            : ok => { if (ok) gate.MarkAvailable(); else gate.MarkUnavailable(); };

        _summaryProcessor = new BatchProcessor<ClusterSummaryMetricItem>(
            WriteSummariesAsync, batchSize, flushInterval, _logger, availabilityCallback: availability);
        _sessionProcessor = new BatchProcessor<SessionMetricItem>(
            WriteSessionsAsync, batchSize, flushInterval, _logger, availabilityCallback: availability);
        _processProcessor = new BatchProcessor<ProcessMetricItem>(
            WriteProcessesAsync, batchSize, flushInterval, _logger, availabilityCallback: availability);
        _connectionProcessor = new BatchProcessor<ConnectionMetricItem>(
            WriteConnectionsAsync, batchSize, flushInterval, _logger, availabilityCallback: availability);
        _lockProcessor = new BatchProcessor<LockMetricItem>(
            WriteLocksAsync, batchSize, flushInterval, _logger, availabilityCallback: availability);
        _infobaseProcessor = new BatchProcessor<InfobaseMetricItem>(
            WriteInfobasesAsync, batchSize, flushInterval, _logger, availabilityCallback: availability);
    }

    public void Start()
    {
        _summaryProcessor.Start();
        _sessionProcessor.Start();
        _processProcessor.Start();
        _connectionProcessor.Start();
        _lockProcessor.Start();
        _infobaseProcessor.Start();
        _logger.LogInformation("Конвейер батчинга метрик запущен");
    }

    public ValueTask EnqueueClusterSummaryAsync(ClusterSummaryMetricItem item, CancellationToken cancellationToken = default)
        => _summaryProcessor.EnqueueAsync(item, cancellationToken);

    public ValueTask EnqueueSessionAsync(SessionMetricItem item, CancellationToken cancellationToken = default)
        => _sessionProcessor.EnqueueAsync(item, cancellationToken);

    public ValueTask EnqueueProcessAsync(ProcessMetricItem item, CancellationToken cancellationToken = default)
        => _processProcessor.EnqueueAsync(item, cancellationToken);

    public ValueTask EnqueueConnectionAsync(ConnectionMetricItem item, CancellationToken cancellationToken = default)
        => _connectionProcessor.EnqueueAsync(item, cancellationToken);

    public ValueTask EnqueueLockAsync(LockMetricItem item, CancellationToken cancellationToken = default)
        => _lockProcessor.EnqueueAsync(item, cancellationToken);

    public ValueTask EnqueueInfobaseAsync(InfobaseMetricItem item, CancellationToken cancellationToken = default)
        => _infobaseProcessor.EnqueueAsync(item, cancellationToken);

    public long DroppedItems =>
        _summaryProcessor.DroppedItems + _sessionProcessor.DroppedItems + _processProcessor.DroppedItems +
        _connectionProcessor.DroppedItems + _lockProcessor.DroppedItems + _infobaseProcessor.DroppedItems;

    private Task WriteSummariesAsync(IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken)
        => WriteGroupsAsync(items, x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId),
            _storage.WriteClusterSummaryBatchAsync, cancellationToken);

    private Task WriteSessionsAsync(IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken)
        => WriteGroupsAsync(items, x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId),
            _storage.WriteSessionsBatchAsync, cancellationToken);

    private Task WriteProcessesAsync(IReadOnlyList<ProcessMetricItem> items, CancellationToken cancellationToken)
        => WriteGroupsAsync(items, x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId),
            _storage.WriteProcessesBatchAsync, cancellationToken);

    private Task WriteConnectionsAsync(IReadOnlyList<ConnectionMetricItem> items, CancellationToken cancellationToken)
        => WriteGroupsAsync(items, x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId),
            _storage.WriteConnectionsBatchAsync, cancellationToken);

    private Task WriteLocksAsync(IReadOnlyList<LockMetricItem> items, CancellationToken cancellationToken)
        => WriteGroupsAsync(items, x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId),
            _storage.WriteLocksBatchAsync, cancellationToken);

    private Task WriteInfobasesAsync(IReadOnlyList<InfobaseMetricItem> items, CancellationToken cancellationToken)
        => WriteGroupsAsync(items, x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId),
            _storage.WriteInfobasesBatchAsync, cancellationToken);

    private async Task WriteGroupsAsync<TItem>(IReadOnlyList<TItem> items, Func<TItem, string> databaseName,
        Func<string, IReadOnlyList<TItem>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        var failed = new List<TItem>();
        var errors = new List<Exception>();
        foreach (var group in items.GroupBy(databaseName))
        {
            var rows = group.ToList();
            try
            {
                await write(group.Key, rows, cancellationToken);
            }
            catch (Exception ex)
            {
                failed.AddRange(rows);
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
            throw new PartialBatchWriteException<TItem>(failed, new AggregateException(errors));
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _summaryProcessor.DisposeAsync();
        await _sessionProcessor.DisposeAsync();
        await _processProcessor.DisposeAsync();
        await _connectionProcessor.DisposeAsync();
        await _lockProcessor.DisposeAsync();
        await _infobaseProcessor.DisposeAsync();
        _logger.LogInformation("Конвейер батчинга метрик остановлен");
    }
}

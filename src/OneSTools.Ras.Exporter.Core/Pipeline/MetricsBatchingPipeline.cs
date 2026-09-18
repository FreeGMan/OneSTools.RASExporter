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

    public MetricsBatchingPipeline(
        IMetricsStorage storage,
        int batchSize = 10000,
        TimeSpan? flushInterval = null,
        ILogger<MetricsBatchingPipeline>? logger = null)
    {
        _storage = storage;
        _logger = logger ?? NullLogger<MetricsBatchingPipeline>.Instance;

        _summaryProcessor = new BatchProcessor<ClusterSummaryMetricItem>(
            WriteSummariesAsync, batchSize, flushInterval, _logger);
        _sessionProcessor = new BatchProcessor<SessionMetricItem>(
            WriteSessionsAsync, batchSize, flushInterval, _logger);
        _processProcessor = new BatchProcessor<ProcessMetricItem>(
            WriteProcessesAsync, batchSize, flushInterval, _logger);
        _connectionProcessor = new BatchProcessor<ConnectionMetricItem>(
            WriteConnectionsAsync, batchSize, flushInterval, _logger);
        _lockProcessor = new BatchProcessor<LockMetricItem>(
            WriteLocksAsync, batchSize, flushInterval, _logger);
    }

    public void Start()
    {
        _summaryProcessor.Start();
        _sessionProcessor.Start();
        _processProcessor.Start();
        _connectionProcessor.Start();
        _lockProcessor.Start();
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

    private async Task WriteSummariesAsync(IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken)
    {
        var groups = items.GroupBy(x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId));
        foreach (var group in groups)
        {
            await _storage.WriteClusterSummaryBatchAsync(group.Key, group.ToList(), cancellationToken);
        }
    }

    private async Task WriteSessionsAsync(IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken)
    {
        var groups = items.GroupBy(x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId));
        foreach (var group in groups)
        {
            await _storage.WriteSessionsBatchAsync(group.Key, group.ToList(), cancellationToken);
        }
    }

    private async Task WriteProcessesAsync(IReadOnlyList<ProcessMetricItem> items, CancellationToken cancellationToken)
    {
        var groups = items.GroupBy(x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId));
        foreach (var group in groups)
        {
            await _storage.WriteProcessesBatchAsync(group.Key, group.ToList(), cancellationToken);
        }
    }

    private async Task WriteConnectionsAsync(IReadOnlyList<ConnectionMetricItem> items, CancellationToken cancellationToken)
    {
        var groups = items.GroupBy(x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId));
        foreach (var group in groups)
        {
            await _storage.WriteConnectionsBatchAsync(group.Key, group.ToList(), cancellationToken);
        }
    }

    private async Task WriteLocksAsync(IReadOnlyList<LockMetricItem> items, CancellationToken cancellationToken)
    {
        var groups = items.GroupBy(x => _storage.GetDatabaseName(x.ClusterName, x.ClusterId));
        foreach (var group in groups)
        {
            await _storage.WriteLocksBatchAsync(group.Key, group.ToList(), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _summaryProcessor.DisposeAsync();
        await _sessionProcessor.DisposeAsync();
        await _processProcessor.DisposeAsync();
        await _connectionProcessor.DisposeAsync();
        await _lockProcessor.DisposeAsync();
        _logger.LogInformation("Конвейер батчинга метрик остановлен");
    }
}

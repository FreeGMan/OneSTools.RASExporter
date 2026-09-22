using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Collectors;

public sealed class ClusterSummaryCollector
{
    private readonly RasAdminClient _client;
    private readonly Guid _clusterId;
    private readonly string _clusterName;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;

    public ClusterSummaryCollector(
        RasAdminClient client,
        Guid clusterId,
        string clusterName,
        MetricsBatchingPipeline pipeline,
        TimeSpan interval,
        ILogger? logger = null)
    {
        _client = client;
        _clusterId = clusterId;
        _clusterName = clusterName;
        _pipeline = pipeline;
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(300) : interval;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CollectOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (_client.Connection.IsClosed)
            {
                throw; // соединение RAS мертво — ClusterMetricsWorker/экспортёр переподключатся
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сборе сводки кластера {Cluster}", _clusterName);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task CollectOnceAsync(CancellationToken cancellationToken = default)
    {
        ClusterInfo info;
        try
        {
            info = await _client.GetClusterInfoAsync(_clusterId, cancellationToken);
        }
        catch
        {
            var clusters = await _client.GetClustersAsync(cancellationToken);
            info = clusters.Count > 0 ? clusters[0] : new ClusterInfo { Uuid = _clusterId, Name = _clusterName };
        }

        var sessions = await _client.GetSessionsAsync(_clusterId, cancellationToken);
        var processes = await _client.GetWorkingProcessesAsync(_clusterId, cancellationToken);
        var connections = await _client.GetConnectionsShortAsync(_clusterId, cancellationToken);
        var locks = await _client.GetLocksAsync(_clusterId, cancellationToken);

        var summary = new ClusterSummaryMetricItem(
            DateTime.UtcNow,
            _clusterId,
            _clusterName,
            info,
            sessions.Count,
            processes.Count,
            connections.Count,
            locks.Count);

        await _pipeline.EnqueueClusterSummaryAsync(summary, cancellationToken);
        _logger.LogDebug("Собрана сводка для кластера {Cluster}: {Sessions} сессий, {Processes} процессов, {Connections} соединений, {Locks} блокировок",
            _clusterName, sessions.Count, processes.Count, connections.Count, locks.Count);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Collectors;

public sealed class ProcessCollector
{
    private readonly RasAdminClient _client;
    private readonly Guid _clusterId;
    private readonly string _clusterName;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly StorageHealthGate? _gate;

    public ProcessCollector(
        RasAdminClient client,
        Guid clusterId,
        string clusterName,
        MetricsBatchingPipeline pipeline,
        TimeSpan interval,
        ILogger? logger = null,
        StorageHealthGate? gate = null)
    {
        _gate = gate;
        _client = client;
        _clusterId = clusterId;
        _clusterName = clusterName;
        _pipeline = pipeline;
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : interval;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_gate is not null)
                    await _gate.WaitUntilReadyAsync(cancellationToken);

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
                _logger.LogError(ex, "Ошибка при сборе рабочих процессов кластера {Cluster}", _clusterName);
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
        var processes = await _client.GetWorkingProcessesAsync(_clusterId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var process in processes)
        {
            await _pipeline.EnqueueProcessAsync(new ProcessMetricItem(now, _clusterId, _clusterName, process), cancellationToken);
        }

        _logger.LogDebug("Собрано {Count} рабочих процессов для кластера {Cluster}", processes.Count, _clusterName);
    }
}

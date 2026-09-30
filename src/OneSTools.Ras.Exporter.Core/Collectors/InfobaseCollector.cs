using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Collectors;

/// <summary>Периодический опрос getInfoBases → pipeline (таблица infobases).</summary>
public sealed class InfobaseCollector
{
    private readonly RasAdminClient _client;
    private readonly Guid _clusterId;
    private readonly string _clusterName;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly StorageHealthGate? _gate;
    private readonly Action<string, Exception?>? _pollStatus;

    public InfobaseCollector(
        RasAdminClient client,
        Guid clusterId,
        string clusterName,
        MetricsBatchingPipeline pipeline,
        TimeSpan interval,
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
                if (_gate is not null)
                    await _gate.WaitUntilReadyAsync(cancellationToken);

                await CollectOnceAsync(cancellationToken);
                _pollStatus?.Invoke("infobases", null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (_client.Connection.IsClosed)
            {
                _pollStatus?.Invoke("infobases", ex);
                throw;
            }
            catch (Exception ex)
            {
                _pollStatus?.Invoke("infobases", ex);
                _logger.LogError(ex, "Ошибка при сборе списка инфобаз кластера {Cluster}", _clusterName);
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
        var infobases = await _client.GetInfoBasesAsync(_clusterId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var infobase in infobases)
        {
            await _pipeline.EnqueueInfobaseAsync(new InfobaseMetricItem(now, _clusterId, _clusterName, infobase), cancellationToken);
        }

        _logger.LogDebug("Собран список инфобаз кластера {Cluster}: {Count} баз", _clusterName, infobases.Count);
    }
}

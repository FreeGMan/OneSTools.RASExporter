using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.Collectors;

/// <summary>Периодический опрос getConnectionListShort → pipeline.</summary>
public sealed class ConnectionCollector
{
    private readonly RasAdminClient _client;
    private readonly Guid _clusterId;
    private readonly string _clusterName;
    private readonly MetricsBatchingPipeline _pipeline;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly StorageHealthGate? _gate;
    private readonly Action<string, Exception?>? _pollStatus;

    public ConnectionCollector(
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
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : interval;
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
                _pollStatus?.Invoke("connections", null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (_client.Connection.IsClosed)
            {
                _pollStatus?.Invoke("connections", ex);
                throw; // соединение RAS мертво — экспортёр выполнит переподключение
            }
            catch (Exception ex)
            {
                _pollStatus?.Invoke("connections", ex);
                _logger.LogError(ex, "Ошибка при сборе соединений кластера {Cluster}", _clusterName);
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
        var connections = await _client.GetConnectionsShortAsync(_clusterId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var connection in connections)
        {
            await _pipeline.EnqueueConnectionAsync(new ConnectionMetricItem(now, _clusterId, _clusterName, connection), cancellationToken);
        }

        _logger.LogDebug("Собрано {Count} соединений для кластера {Cluster}", connections.Count, _clusterName);
    }
}

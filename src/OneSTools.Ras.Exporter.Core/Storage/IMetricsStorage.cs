using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OneSTools.Ras.Exporter.Core.Storage;

/// <summary>Абстракция хранилища метрик: авто-создание схем, пакетная запись, зонд доступности.</summary>
public interface IMetricsStorage
{
    string GetDatabaseName(string clusterName, Guid clusterId);

    /// <summary>Лёгкий зонд доступности (SELECT 1): true = хранилище живо. Используется StorageHealthGate.</summary>
    Task<bool> ProbeAsync(CancellationToken cancellationToken = default);

    Task InitializeClusterDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);

    Task WriteClusterSummaryBatchAsync(string databaseName, IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken = default);

    Task WriteSessionsBatchAsync(string databaseName, IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken = default);

    Task WriteProcessesBatchAsync(string databaseName, IReadOnlyList<ProcessMetricItem> items, CancellationToken cancellationToken = default);

    Task WriteConnectionsBatchAsync(string databaseName, IReadOnlyList<ConnectionMetricItem> items, CancellationToken cancellationToken = default);

    Task WriteLocksBatchAsync(string databaseName, IReadOnlyList<LockMetricItem> items, CancellationToken cancellationToken = default);

    Task WriteInfobasesBatchAsync(string databaseName, IReadOnlyList<InfobaseMetricItem> items, CancellationToken cancellationToken = default);
}

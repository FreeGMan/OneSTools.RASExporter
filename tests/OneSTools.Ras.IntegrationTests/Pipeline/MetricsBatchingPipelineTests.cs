using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Pipeline;

public sealed class MetricsBatchingPipelineTests
{
    private sealed class MockMetricsStorage : IMetricsStorage
    {
        public ConcurrentBag<SessionMetricItem> WrittenSessions { get; } = new();
        public ConcurrentBag<ClusterSummaryMetricItem> WrittenSummaries { get; } = new();
        public ConcurrentBag<ProcessMetricItem> WrittenProcesses { get; } = new();
        public ConcurrentBag<ConnectionMetricItem> WrittenConnections { get; } = new();
        public ConcurrentBag<LockMetricItem> WrittenLocks { get; } = new();

        public string GetDatabaseName(string clusterName, Guid clusterId) => $"ras_cluster_{clusterName}";

        public Task<bool> ProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task InitializeClusterDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteClusterSummaryBatchAsync(string databaseName, IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenSummaries.Add(item);
            return Task.CompletedTask;
        }

        public Task WriteSessionsBatchAsync(string databaseName, IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenSessions.Add(item);
            return Task.CompletedTask;
        }

        public Task WriteProcessesBatchAsync(string databaseName, IReadOnlyList<ProcessMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenProcesses.Add(item);
            return Task.CompletedTask;
        }

        public Task WriteConnectionsBatchAsync(string databaseName, IReadOnlyList<ConnectionMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenConnections.Add(item);
            return Task.CompletedTask;
        }

        public Task WriteLocksBatchAsync(string databaseName, IReadOnlyList<LockMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenLocks.Add(item);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Pipeline_Buffers_And_Flushes_All_Metric_Types()
    {
        var storage = new MockMetricsStorage();
        await using var pipeline = new MetricsBatchingPipeline(storage, batchSize: 50, flushInterval: TimeSpan.FromMilliseconds(200));

        pipeline.Start();

        var clusterId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        for (int i = 0; i < 20; i++)
        {
            await pipeline.EnqueueSessionAsync(new SessionMetricItem(now, clusterId, "ClusterA", new SessionInfo { Id = i, UserName = $"User_{i}" }));
            await pipeline.EnqueueProcessAsync(new ProcessMetricItem(now, clusterId, "ClusterA", new ProcessInfo { Pid = $"{1000 + i}" }));
            await pipeline.EnqueueConnectionAsync(new ConnectionMetricItem(now, clusterId, "ClusterA", new ConnectionInfo { Id = i }));
            await pipeline.EnqueueLockAsync(new LockMetricItem(now, clusterId, "ClusterA", new LockInfo { Description = $"Lock_{i}" }));
        }

        // Wait for timer flush
        await Task.Delay(500);

        Assert.Equal(20, storage.WrittenSessions.Count);
        Assert.Equal(20, storage.WrittenProcesses.Count);
        Assert.Equal(20, storage.WrittenConnections.Count);
        Assert.Equal(20, storage.WrittenLocks.Count);
    }
}

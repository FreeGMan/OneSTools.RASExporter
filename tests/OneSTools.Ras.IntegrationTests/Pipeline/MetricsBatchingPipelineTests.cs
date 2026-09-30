using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.Collectors;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;
using OneSTools.Ras.Exporter.Services.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        public ConcurrentBag<InfobaseMetricItem> WrittenInfobases { get; } = new();
        public ConcurrentDictionary<string, int> SessionWriteAttempts { get; } = new();
        public Func<string, int, bool>? FailSessionWrite { get; set; }
        public int InitializationAttempts;
        public bool FailFirstInitialization { get; set; }
        public TaskCompletionSource<bool> InitializationReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string GetDatabaseName(string clusterName, Guid clusterId) => $"ras_cluster_{clusterName}";

        public Task<bool> ProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task InitializeClusterDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref InitializationAttempts) == 1 && FailFirstInitialization)
                throw new InvalidOperationException("temporary schema failure");
            InitializationReady.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task WriteClusterSummaryBatchAsync(string databaseName, IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenSummaries.Add(item);
            return Task.CompletedTask;
        }

        public Task WriteSessionsBatchAsync(string databaseName, IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken = default)
        {
            int attempt = SessionWriteAttempts.AddOrUpdate(databaseName, 1, (_, value) => value + 1);
            if (FailSessionWrite?.Invoke(databaseName, attempt) == true)
                throw new InvalidOperationException("temporary write failure");
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

        public Task WriteInfobasesBatchAsync(string databaseName, IReadOnlyList<InfobaseMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenInfobases.Add(item);
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
            await pipeline.EnqueueInfobaseAsync(new InfobaseMetricItem(now, clusterId, "ClusterA", new InfobaseInfo { Name = $"ib_{i}" }));
        }

        // Ждём полный сброс всех потоков (без жёсткого Delay: гонки с планировщиком при
        // параллельном запуске коллекций давали флук).
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (storage.WrittenSessions.Count < 20 || storage.WrittenProcesses.Count < 20 ||
               storage.WrittenConnections.Count < 20 || storage.WrittenLocks.Count < 20 ||
               storage.WrittenInfobases.Count < 20)
        {
            await Task.Delay(50, wait.Token);
        }

        Assert.Equal(20, storage.WrittenSessions.Count);
        Assert.Equal(20, storage.WrittenProcesses.Count);
        Assert.Equal(20, storage.WrittenConnections.Count);
        Assert.Equal(20, storage.WrittenLocks.Count);
        Assert.Equal(20, storage.WrittenInfobases.Count);
    }

    [Fact]
    public async Task Retry_Only_Writes_Previously_Failed_Groups()
    {
        var storage = new MockMetricsStorage
        {
            FailSessionWrite = (database, attempt) => database == "ras_cluster_B" && attempt == 1
        };
        await using var pipeline = new MetricsBatchingPipeline(storage, batchSize: 2, flushInterval: TimeSpan.FromMilliseconds(50));
        pipeline.Start();
        var timestamp = DateTime.UtcNow;
        await pipeline.EnqueueSessionAsync(new SessionMetricItem(timestamp, Guid.NewGuid(), "A", new SessionInfo()));
        await pipeline.EnqueueSessionAsync(new SessionMetricItem(timestamp, Guid.NewGuid(), "B", new SessionInfo()));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (storage.WrittenSessions.Count < 2)
            await Task.Delay(20, timeout.Token);

        Assert.Equal(1, storage.SessionWriteAttempts["ras_cluster_A"]);
        Assert.Equal(2, storage.SessionWriteAttempts["ras_cluster_B"]);
        Assert.Equal(0, pipeline.DroppedItems);
    }

    [Fact]
    public async Task Worker_Retries_Schema_Initialization_Before_Polling()
    {
        var storage = new MockMetricsStorage { FailFirstInitialization = true };
        await using var client = new RasAdminClient("localhost");
        await using var pipeline = new MetricsBatchingPipeline(storage);
        var worker = new ClusterMetricsWorker(client, Guid.NewGuid(), "A", pipeline, storage,
            new ClusterCollectorIntervals(TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromHours(1),
                TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var running = worker.RunAsync(stop.Token);
        await storage.InitializationReady.Task.WaitAsync(stop.Token);

        stop.Cancel();
        await running;
        Assert.Equal(2, storage.InitializationAttempts);
    }

    [Fact]
    public async Task Unrecoverable_Batch_Loss_Is_Reported_In_Health()
    {
        var storage = new MockMetricsStorage { FailSessionWrite = (_, _) => true };
        await using var pipeline = new MetricsBatchingPipeline(storage, batchSize: 1, flushInterval: TimeSpan.FromMilliseconds(20));
        await using var gate = new StorageHealthGate(_ => Task.FromResult(true));
        var registry = new RasExporterHealthRegistry();
        registry.MarkConnected("server/cluster");
        registry.ConfigurePolls("server/cluster", new Dictionary<string, TimeSpan> { ["sessions"] = TimeSpan.FromSeconds(5) });
        registry.RecordPoll("server/cluster", "sessions", null);
        pipeline.Start();
        await pipeline.EnqueueSessionAsync(new SessionMetricItem(DateTime.UtcNow, Guid.NewGuid(), "A", new SessionInfo()));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (pipeline.DroppedItems == 0)
            await Task.Delay(20, timeout.Token);

        var check = new RasExporterHealthCheck(registry, gate, pipeline);
        var health = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Contains("Потеряно элементов", health.Description);
    }
}

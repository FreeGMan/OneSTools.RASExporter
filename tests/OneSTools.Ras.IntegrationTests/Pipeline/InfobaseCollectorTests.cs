using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Exporter.Core.Collectors;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Pipeline;

public sealed class InfobaseCollectorTests : IAsyncLifetime
{
    private static readonly Guid MockClusterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MockInfobaseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class RecordingStorage : IMetricsStorage
    {
        public ConcurrentBag<InfobaseMetricItem> WrittenInfobases { get; } = new();

        public string GetDatabaseName(string clusterName, Guid clusterId) => $"ras_cluster_{clusterName}";
        public Task<bool> ProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task InitializeClusterDatabaseAsync(string databaseName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteClusterSummaryBatchAsync(string databaseName, IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteSessionsBatchAsync(string databaseName, IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteProcessesBatchAsync(string databaseName, IReadOnlyList<ProcessMetricItem> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteConnectionsBatchAsync(string databaseName, IReadOnlyList<ConnectionMetricItem> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteLocksBatchAsync(string databaseName, IReadOnlyList<LockMetricItem> items, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteInfobasesBatchAsync(string databaseName, IReadOnlyList<InfobaseMetricItem> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) WrittenInfobases.Add(item);
            return Task.CompletedTask;
        }
    }

    private readonly CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private int _port;

    public async Task InitializeAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _serverTask = Task.Run(() => Program.RunAsync(new[] { _port.ToString() }, _serverCts.Token));

        using var probe = new TcpClient();
        for (int i = 0; i < 50; i++)
        {
            try
            {
                await probe.ConnectAsync(IPAddress.Loopback, _port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }
    }

    public async Task DisposeAsync()
    {
        _serverCts.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
        }
        _serverCts.Dispose();
    }

    private async Task<(RasAdminClient Client, RecordingStorage Storage, MetricsBatchingPipeline Pipeline)> OpenAsync()
    {
        var storage = new RecordingStorage();
        var pipeline = new MetricsBatchingPipeline(storage, batchSize: 10, flushInterval: TimeSpan.FromMilliseconds(50));
        pipeline.Start();

        var client = new RasAdminClient("127.0.0.1", _port, NullLogger<RasAdminClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("admin", "pwd", cancellationToken: cts.Token);
        await client.AuthenticateClusterAsync(MockClusterId, "clusterAdmin", "clusterPwd", cts.Token);
        return (client, storage, pipeline);
    }

    [Fact]
    public async Task CollectOnce_Writes_Infobase_Snapshot_To_Storage()
    {
        var (client, storage, pipeline) = await OpenAsync();
        await using (pipeline)
        await using (client)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var collector = new InfobaseCollector(client, MockClusterId, "MockCluster", pipeline, TimeSpan.FromSeconds(300));

            await collector.CollectOnceAsync(cts.Token);

            var item = await WaitForResourceAsync(storage);
            Assert.Equal(MockClusterId, item.ClusterId);
            Assert.Equal("MockCluster", item.ClusterName);
            Assert.Equal(MockInfobaseId, item.Infobase.Uuid);
            Assert.Equal("mock_db", item.Infobase.Name);
            Assert.Equal("Mock Infobase", item.Infobase.Descr);
            Assert.Equal("mock-server", item.Infobase.DbServer);
            Assert.Equal(1, item.Infobase.SecurityLevel);
            Assert.Equal(DateTimeKind.Utc, item.Timestamp.Kind);
        }
    }

    [Fact]
    public async Task RunLoop_Reports_Infobases_Poll_Status()
    {
        var (client, storage, pipeline) = await OpenAsync();
        await using (pipeline)
        await using (client)
        {
            string? reportedCollector = null;
            Exception? reportedError = new InvalidOperationException("not polled");
            var collector = new InfobaseCollector(client, MockClusterId, "MockCluster", pipeline, TimeSpan.FromSeconds(1),
                pollStatus: (name, error) => { reportedCollector = name; reportedError = error; });

            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var running = collector.RunAsync(stop.Token);
            while (reportedCollector is null && !stop.IsCancellationRequested)
                await Task.Delay(20);
            stop.Cancel();
            try { await running; } catch (OperationCanceledException) { }

            Assert.Equal("infobases", reportedCollector);
            Assert.Null(reportedError);
            Assert.Equal("mock_db", (await WaitForResourceAsync(storage)).Infobase.Name);
        }
    }

    private static async Task<InfobaseMetricItem> WaitForResourceAsync(RecordingStorage storage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (storage.WrittenInfobases.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.True(storage.WrittenInfobases.TryTake(out InfobaseMetricItem? item), "инфобаза не записана в хранилище");
        return item!;
    }
}

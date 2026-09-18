using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using OneSTools.Ras.Exporter.Core.Collectors;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.MockServer;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.ClickHouse;

public sealed class FullExportPipelineTests : IAsyncLifetime
{
    private const string ConnectionString = "Host=clickhouse;Port=8123;Username=default;Password=clickhouse;";
    private const string TestDatabase = "ras_e2e_pipeline_test";

    private readonly CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private int _port;

    private static async Task<(bool Available, string? Error)> CheckClickHouseAvailabilityAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var conn = new ClickHouseConnection(ConnectionString);
            await conn.OpenAsync(cts.Token);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var res = await cmd.ExecuteScalarAsync(cts.Token);
            if (res is not null && Convert.ToInt32(res) == 1)
            {
                return (true, null);
            }
            return (false, "Запрос 'SELECT 1' не вернул ожидаемого результата.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task InitializeAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _serverTask = Task.Run(() => Program.RunAsync(new[] { _port.ToString() }, _serverCts.Token));

        using var client = new TcpClient();
        for (int i = 0; i < 50; i++)
        {
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, _port);
                client.Close();
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

    [SkippableFact]
    public async Task EndToEnd_MockServer_To_Pipeline_To_ClickHouse_Works()
    {
        var (available, error) = await CheckClickHouseAvailabilityAsync();
        Skip.If(!available, $"ClickHouse недоступен ({error}). Строка подключения: '{ConnectionString}'.");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // 1. Storage
        var storage = new ClickHouseMetricsStorage(
            ConnectionString,
            databaseTemplate: TestDatabase,
            autoCreateDatabases: true,
            autoCreateTables: true,
            batchSize: 10,
            NullLogger<ClickHouseMetricsStorage>.Instance);

        await storage.InitializeClusterDatabaseAsync(TestDatabase, cts.Token);

        // 2. Pipeline
        await using var pipeline = new MetricsBatchingPipeline(
            storage,
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(1),
            NullLogger<MetricsBatchingPipeline>.Instance);

        pipeline.Start();

        // 3. Client
        await using var client = new RasAdminClient("127.0.0.1", _port, NullLogger<RasAdminClient>.Instance);
        await client.ConnectAsync("admin", "pwd", cancellationToken: cts.Token);

        var clusters = await client.GetClustersAsync(cts.Token);
        Assert.NotEmpty(clusters);
        var cluster = clusters[0];

        // 4. Run collectors once
        var summaryCollector = new ClusterSummaryCollector(client, cluster.Uuid, cluster.Name, pipeline, TimeSpan.FromSeconds(10));
        var sessionCollector = new SessionCollector(client, cluster.Uuid, cluster.Name, pipeline, TimeSpan.FromSeconds(10));
        var processCollector = new ProcessCollector(client, cluster.Uuid, cluster.Name, pipeline, TimeSpan.FromSeconds(10));
        var connectionCollector = new ConnectionCollector(client, cluster.Uuid, cluster.Name, pipeline, TimeSpan.FromSeconds(10));
        var lockCollector = new LockCollector(client, cluster.Uuid, cluster.Name, pipeline, TimeSpan.FromSeconds(10));

        await summaryCollector.CollectOnceAsync(cts.Token);
        await sessionCollector.CollectOnceAsync(cts.Token);
        await processCollector.CollectOnceAsync(cts.Token);
        await connectionCollector.CollectOnceAsync(cts.Token);
        await lockCollector.CollectOnceAsync(cts.Token);

        // 5. Allow pipeline to flush (wait for flush interval + batch processing)
        await Task.Delay(1500, cts.Token);

        // 6. Verify data in ClickHouse
        using var conn = new ClickHouseConnection(ConnectionString);
        await conn.OpenAsync(cts.Token);

        async Task<ulong> GetCountAsync(string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT count() FROM `{TestDatabase}`.`{table}`";
            var res = await cmd.ExecuteScalarAsync(cts.Token);
            return Convert.ToUInt64(res);
        }

        Assert.True(await GetCountAsync(ClickHouseTableDefinitions.ClusterSummaryTableName) >= 1);
        Assert.True(await GetCountAsync(ClickHouseTableDefinitions.SessionsTableName) >= 1);
        Assert.True(await GetCountAsync(ClickHouseTableDefinitions.ProcessesTableName) >= 1);
        Assert.True(await GetCountAsync(ClickHouseTableDefinitions.ConnectionsTableName) >= 1);
        Assert.True(await GetCountAsync(ClickHouseTableDefinitions.LocksTableName) >= 1);
    }
}

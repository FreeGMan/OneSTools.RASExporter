using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using OneSTools.Ras.Exporter.Core.Storage;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.ClickHouse;

public sealed class ClickHouseMetricsStorageTests
{
    private const string ConnectionString = "Host=clickhouse;Port=8123;Username=default;Password=clickhouse;";
    private const string TestDatabase = "ras_integration_test";

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

    [SkippableFact]
    public async Task Can_Initialize_Database_And_Insert_All_Metric_Types()
    {
        var (available, error) = await CheckClickHouseAvailabilityAsync();
        Skip.If(!available, $"ClickHouse недоступен ({error}). Строка подключения: '{ConnectionString}'.");

        var storage = new ClickHouseMetricsStorage(
            ConnectionString,
            databaseTemplate: TestDatabase,
            autoCreateDatabases: true,
            autoCreateTables: true,
            batchSize: 100,
            NullLogger<ClickHouseMetricsStorage>.Instance);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // 1. Initialize schema
        await storage.InitializeClusterDatabaseAsync(TestDatabase, cts.Token);

        var clusterId = Guid.NewGuid();
        string clusterName = "TestCluster";
        var now = DateTime.UtcNow;

        // 2. Insert Cluster Summary
        var summary = new ClusterSummaryMetricItem(
            now,
            clusterId,
            clusterName,
            new ClusterInfo
            {
                Uuid = clusterId,
                Name = clusterName,
                Host = "srv-1c-test",
                Port = 1541
            },
            SessionsCount: 5,
            ProcessesCount: 2,
            ConnectionsCount: 10,
            LocksCount: 1);

        await storage.WriteClusterSummaryBatchAsync(TestDatabase, new[] { summary }, cts.Token);

        // 3. Insert Session
        var session = new SessionMetricItem(
            now,
            clusterId,
            clusterName,
            new SessionInfo
            {
                Uuid = Guid.NewGuid(),
                Id = 100,
                UserName = "TestUser",
                AppId = "1CV8C",
                Host = "client-01",
                StartedAt = now.AddHours(-1),
                LastActiveAt = now
            });

        await storage.WriteSessionsBatchAsync(TestDatabase, new[] { session }, cts.Token);

        // 4. Insert Process
        var process = new ProcessMetricItem(
            now,
            clusterId,
            clusterName,
            new ProcessInfo
            {
                Uuid = Guid.NewGuid(),
                Host = "srv-1c-test",
                Port = 1560,
                Pid = "9999",
                Running = true,
                Enable = true
            });

        await storage.WriteProcessesBatchAsync(TestDatabase, new[] { process }, cts.Token);

        // 5. Insert Connection
        var connection = new ConnectionMetricItem(
            now,
            clusterId,
            clusterName,
            new ConnectionInfo
            {
                Uuid = Guid.NewGuid(),
                Id = 200,
                Application = "1CV8C",
                Host = "client-01",
                ConnectedAt = now.AddHours(-1)
            });

        await storage.WriteConnectionsBatchAsync(TestDatabase, new[] { connection }, cts.Token);

        // 6. Insert Lock
        var lockItem = new LockMetricItem(
            now,
            clusterId,
            clusterName,
            new LockInfo
            {
                ConnectionId = connection.Connection.Uuid,
                SessionId = session.Session.Uuid,
                ObjectId = Guid.NewGuid(),
                LockedAt = now,
                Description = "Exclusive Test Lock"
            });

        await storage.WriteLocksBatchAsync(TestDatabase, new[] { lockItem }, cts.Token);

        // 7. Verify counts in ClickHouse
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

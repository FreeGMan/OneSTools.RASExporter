using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Exporter.Core.Storage;

namespace OneSTools.Ras.Exporter.Core.ClickHouse;

/// <summary>Хранилище метрик в ClickHouse: авто-схемы, батчи через ClickHouseBulkCopy, зонд ProbeAsync (docs/SECURITY.md §3).</summary>
public sealed class ClickHouseMetricsStorage : IMetricsStorage
{
    private readonly string _connectionString;
    private readonly string _databaseTemplate;
    private readonly bool _autoCreateDatabases;
    private readonly bool _autoCreateTables;
    private readonly int _batchSize;
    private readonly ILogger<ClickHouseMetricsStorage> _logger;

    public ClickHouseMetricsStorage(
        string connectionString,
        string databaseTemplate = "ras_cluster_{ClusterName}",
        bool autoCreateDatabases = true,
        bool autoCreateTables = true,
        int batchSize = 10000,
        ILogger<ClickHouseMetricsStorage>? logger = null)
    {
        _connectionString = connectionString;
        _databaseTemplate = string.IsNullOrEmpty(databaseTemplate) ? "ras_cluster_{ClusterName}" : databaseTemplate;
        _autoCreateDatabases = autoCreateDatabases;
        _autoCreateTables = autoCreateTables;
        _batchSize = batchSize > 0 ? batchSize : 10000;
        _logger = logger ?? NullLogger<ClickHouseMetricsStorage>.Instance;
    }

    public string GetDatabaseName(string clusterName, Guid clusterId)
    {
        string dbName = _databaseTemplate
            .Replace("{ClusterName}", ClickHouseDatabaseNamer.Slugify(clusterName))
            .Replace("{ClusterId}", clusterId.ToString("N"));

        dbName = ClickHouseDatabaseNamer.Slugify(dbName);
        return dbName.Length > 128 ? dbName[..128].TrimEnd('_') : dbName;
    }

    /// <summary>Вторая линия защиты (T7/S4): identifier попадает в DDL текстом — пропускаем только
    /// [a-z0-9_] длиной ≤128, независимо от источника (Slugify должен гарантировать это всегда).</summary>
    private static string ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrEmpty(databaseName) || databaseName.Length > 128)
            throw new ArgumentException($"Недопустимое имя базы данных (длина): '{databaseName}'", nameof(databaseName));

        foreach (char c in databaseName)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                throw new ArgumentException($"Недопустимые символы в имени базы данных: '{databaseName}'", nameof(databaseName));
        }

        return databaseName;
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            return res is not null && Convert.ToInt32(res) == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Зонд ClickHouse неудачен");
            return false;
        }
    }

    public async Task InitializeClusterDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);

        if (!_autoCreateDatabases && !_autoCreateTables) return;

        _logger.LogInformation("Инициализация схемы ClickHouse для базы данных: {DbName}", databaseName);

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (_autoCreateDatabases)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = ClickHouseTableDefinitions.CreateDatabase(databaseName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (_autoCreateTables)
        {
            string[] tableDdls =
            {
                ClickHouseTableDefinitions.CreateClusterSummaryTable(databaseName),
                ClickHouseTableDefinitions.CreateSessionsTable(databaseName),
                ClickHouseTableDefinitions.CreateProcessesTable(databaseName),
                ClickHouseTableDefinitions.CreateConnectionsTable(databaseName),
                ClickHouseTableDefinitions.CreateLocksTable(databaseName),
                ClickHouseTableDefinitions.CreateInfobasesTable(databaseName)
            };

            foreach (var ddl in tableDdls)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    public async Task WriteClusterSummaryBatchAsync(string databaseName, IReadOnlyList<ClusterSummaryMetricItem> items, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);


        if (items.Count == 0) return;

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new[]
        {
            "_time", "cluster_id", "cluster_name", "host", "port", "expiration_timeout", "lifetime_limit",
            "max_memory_size", "max_memory_time_limit", "security_level", "session_fault_tolerance_level",
            "load_balancing_mode", "errors_count_threshold", "kill_problem_processes",
            "sessions_count", "processes_count", "connections_count", "locks_count"
        };

        var rows = new List<object[]>(items.Count);
        foreach (var item in items)
        {
            var info = item.ClusterInfo;
            rows.Add(new object[]
            {
                item.Timestamp,
                item.ClusterId,
                item.ClusterName,
                info.Host,
                (ushort)info.Port,
                info.ExpirationTimeout,
                info.LifetimeLimit,
                info.MaxMemorySize,
                info.MaxMemoryTimeLimit,
                info.SecurityLevel,
                info.SessionFaultToleranceLevel,
                info.LoadBalancingMode,
                info.ErrorsCountThreshold,
                (byte)(info.KillProblemProcesses ? 1 : 0),
                (uint)item.SessionsCount,
                (uint)item.ProcessesCount,
                (uint)item.ConnectionsCount,
                (uint)item.LocksCount
            });
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = $"`{databaseName}`.`{ClickHouseTableDefinitions.ClusterSummaryTableName}`",
            ColumnNames = columns,
            BatchSize = _batchSize
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    public async Task WriteSessionsBatchAsync(string databaseName, IReadOnlyList<SessionMetricItem> items, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);


        if (items.Count == 0) return;

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new[]
        {
            "_time", "cluster_id", "cluster_name", "uuid", "id", "user_name", "app_id", "host",
            "client_ip_address", "infobase_id", "connection_id", "process_id", "started_at",
            "last_active_at", "hibernate", "passive_session_hibernate_time", "hibernate_session_terminate_time",
            "blocked_by_dbms", "blocked_by_ls", "bytes_all", "bytes_last5min", "calls_all",
            "calls_last5min", "dbms_bytes_all", "dbms_bytes_last5min", "db_proc_info", "db_proc_took",
            "db_proc_took_at", "duration_all", "duration_all_dbms", "duration_current", "duration_current_dbms",
            "duration_last_5_min", "duration_last_5_min_dbms", "memory_current", "memory_last5min", "memory_total",
            "read_current", "read_last5min", "read_total", "write_current", "write_last5min", "write_total",
            "duration_current_service", "duration_last5min_service", "duration_all_service",
            "current_service_name", "cpu_time_current", "cpu_time_last5min", "cpu_time_total",
            "data_separation", "locale", "licenses_count"
        };

        var rows = new List<object[]>(items.Count);
        foreach (var item in items)
        {
            var s = item.Session;
            rows.Add(new object[]
            {
                item.Timestamp,
                item.ClusterId,
                item.ClusterName,
                s.Uuid,
                s.Id,
                s.UserName,
                s.AppId,
                s.Host,
                s.ClientIpAddress,
                s.InfobaseId,
                s.ConnectionId,
                s.ProcessId,
                s.StartedAt == default ? item.Timestamp : s.StartedAt,
                s.LastActiveAt == default ? item.Timestamp : s.LastActiveAt,
                (byte)(s.Hibernate ? 1 : 0),
                s.PassiveSessionHibernateTime,
                s.HibernateSessionTerminateTime,
                s.BlockedByDbms,
                s.BlockedByLs,
                s.BytesAll,
                s.BytesLast5Min,
                s.CallsAll,
                s.CallsLast5Min,
                s.DbmsBytesAll,
                s.DbmsBytesLast5Min,
                s.DbProcInfo,
                s.DbProcTook,
                s.DbProcTookAt == default ? item.Timestamp : s.DbProcTookAt,
                s.DurationAll,
                s.DurationAllDbms,
                s.DurationCurrent,
                s.DurationCurrentDbms,
                s.DurationLast5Min,
                s.DurationLast5MinDbms,
                s.MemoryCurrent,
                s.MemoryLast5Min,
                s.MemoryTotal,
                s.ReadCurrent,
                s.ReadLast5Min,
                s.ReadTotal,
                s.WriteCurrent,
                s.WriteLast5Min,
                s.WriteTotal,
                s.DurationCurrentService,
                s.DurationLast5MinService,
                s.DurationAllService,
                s.CurrentServiceName,
                s.CpuTimeCurrent,
                s.CpuTimeLast5Min,
                s.CpuTimeTotal,
                s.DataSeparation,
                s.Locale,
                (uint)s.Licenses.Count
            });
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = $"`{databaseName}`.`{ClickHouseTableDefinitions.SessionsTableName}`",
            ColumnNames = columns,
            BatchSize = _batchSize
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    public async Task WriteProcessesBatchAsync(string databaseName, IReadOnlyList<ProcessMetricItem> items, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);


        if (items.Count == 0) return;

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new[]
        {
            "_time", "cluster_id", "cluster_name", "uuid", "host", "port", "pid", "running",
            "enable", "use", "started_at", "connections", "capacity", "memory_size", "memory_excess_time",
            "selection_size", "available_performance", "avg_call_time", "avg_db_call_time",
            "avg_lock_call_time", "avg_server_call_time", "avg_back_call_time", "avg_threads", "licenses_count"
        };

        var rows = new List<object[]>(items.Count);
        foreach (var item in items)
        {
            var p = item.Process;
            rows.Add(new object[]
            {
                item.Timestamp,
                item.ClusterId,
                item.ClusterName,
                p.Uuid,
                p.Host,
                (ushort)p.Port,
                p.Pid,
                (byte)(p.Running ? 1 : 0),
                (byte)(p.Enable ? 1 : 0),
                (byte)(p.Use ? 1 : 0),
                p.StartedAt == default ? item.Timestamp : p.StartedAt,
                p.Connections,
                p.Capacity,
                p.MemorySize,
                p.MemoryExcessTime,
                p.SelectionSize,
                p.AvailablePerformance,
                p.AvgCallTime,
                p.AvgDbCallTime,
                p.AvgLockCallTime,
                p.AvgServerCallTime,
                p.AvgBackCallTime,
                p.AvgThreads,
                (uint)p.Licenses.Count
            });
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = $"`{databaseName}`.`{ClickHouseTableDefinitions.ProcessesTableName}`",
            ColumnNames = columns,
            BatchSize = _batchSize
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    public async Task WriteConnectionsBatchAsync(string databaseName, IReadOnlyList<ConnectionMetricItem> items, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);


        if (items.Count == 0) return;

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new[]
        {
            "_time", "cluster_id", "cluster_name", "uuid", "id", "application", "host",
            "connected_at", "infobase_id", "process_id", "session_id", "blocked_by_ls"
        };

        var rows = new List<object[]>(items.Count);
        foreach (var item in items)
        {
            var c = item.Connection;
            rows.Add(new object[]
            {
                item.Timestamp,
                item.ClusterId,
                item.ClusterName,
                c.Uuid,
                c.Id,
                c.Application,
                c.Host,
                c.ConnectedAt == default ? item.Timestamp : c.ConnectedAt,
                c.InfobaseId,
                c.ProcessId,
                c.SessionId,
                c.BlockedByLs
            });
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = $"`{databaseName}`.`{ClickHouseTableDefinitions.ConnectionsTableName}`",
            ColumnNames = columns,
            BatchSize = _batchSize
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    public async Task WriteLocksBatchAsync(string databaseName, IReadOnlyList<LockMetricItem> items, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);


        if (items.Count == 0) return;

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new[]
        {
            "_time", "cluster_id", "cluster_name", "connection_id", "session_id", "object_id",
            "locked_at", "description"
        };

        var rows = new List<object[]>(items.Count);
        foreach (var item in items)
        {
            var l = item.Lock;
            rows.Add(new object[]
            {
                item.Timestamp,
                item.ClusterId,
                item.ClusterName,
                l.ConnectionId,
                l.SessionId,
                l.ObjectId,
                l.LockedAt == default ? item.Timestamp : l.LockedAt,
                l.Description
            });
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = $"`{databaseName}`.`{ClickHouseTableDefinitions.LocksTableName}`",
            ColumnNames = columns,
            BatchSize = _batchSize
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    public async Task WriteInfobasesBatchAsync(string databaseName, IReadOnlyList<InfobaseMetricItem> items, CancellationToken cancellationToken = default)
    {
        ValidateDatabaseName(databaseName);

        if (items.Count == 0) return;

        using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new[]
        {
            "_time", "cluster_id", "cluster_name", "infobase_id", "name", "descr", "locale", "date_offset",
            "dbms", "db_server", "db_name", "db_user", "security_level", "license_distribution",
            "sessions_deny", "scheduled_jobs_deny", "external_session_manager_required",
            "security_profile_name", "safe_mode_security_profile_name", "reserve_working_processes",
            "denied_from", "denied_to", "denied_message", "denied_parameter", "permission_code"
        };

        var rows = new List<object?[]>(items.Count);
        foreach (var item in items)
        {
            var ib = item.Infobase;
            rows.Add(new object?[]
            {
                item.Timestamp,
                item.ClusterId,
                item.ClusterName,
                ib.Uuid,
                ib.Name,
                ib.Descr,
                ib.Locale,
                ib.DateOffset,
                ib.Dbms,
                ib.DbServer,
                ib.DbName,
                ib.DbUser,
                ib.SecurityLevel,
                ib.LicenseDistribution,
                (byte)(ib.SessionsDeny ? 1 : 0),
                (byte)(ib.ScheduledJobsDeny ? 1 : 0),
                (byte)(ib.ExternalSessionManagerRequired ? 1 : 0),
                ib.SecurityProfileName,
                ib.SafeModeSecurityProfileName,
                (byte)(ib.ReserveWorkingProcesses ? 1 : 0),
                ib.DeniedFrom == default ? null : ib.DeniedFrom,
                ib.DeniedTo == default ? null : ib.DeniedTo,
                ib.DeniedMessage,
                ib.DeniedParameter,
                ib.PermissionCode
            });
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = $"`{databaseName}`.`{ClickHouseTableDefinitions.InfobasesTableName}`",
            ColumnNames = columns,
            BatchSize = _batchSize
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }
}

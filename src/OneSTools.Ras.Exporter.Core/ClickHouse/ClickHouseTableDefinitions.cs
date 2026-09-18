namespace OneSTools.Ras.Exporter.Core.ClickHouse;

public static class ClickHouseTableDefinitions
{
    public const string ClusterSummaryTableName = "cluster_summary";
    public const string SessionsTableName = "sessions";
    public const string ProcessesTableName = "processes";
    public const string ConnectionsTableName = "connections";
    public const string LocksTableName = "locks";

    public static string CreateDatabase(string databaseName) =>
        $"CREATE DATABASE IF NOT EXISTS `{databaseName}`";

    public static string CreateClusterSummaryTable(string databaseName) =>
        $@"CREATE TABLE IF NOT EXISTS `{databaseName}`.`{ClusterSummaryTableName}`
        (
            `_time` DateTime64(3) CODEC(DoubleDelta, ZSTD),
            `cluster_id` UUID,
            `cluster_name` LowCardinality(String),
            `host` LowCardinality(String),
            `port` UInt16,
            `expiration_timeout` Int32,
            `lifetime_limit` Int32,
            `max_memory_size` Int32,
            `max_memory_time_limit` Int32,
            `security_level` Int32,
            `session_fault_tolerance_level` Int32,
            `load_balancing_mode` Int32,
            `errors_count_threshold` Int32,
            `kill_problem_processes` UInt8,
            `sessions_count` UInt32,
            `processes_count` UInt32,
            `connections_count` UInt32,
            `locks_count` UInt32
        )
        ENGINE = MergeTree()
        PARTITION BY toYYYYMM(_time)
        ORDER BY (_time, cluster_id);";

    public static string CreateSessionsTable(string databaseName) =>
        $@"CREATE TABLE IF NOT EXISTS `{databaseName}`.`{SessionsTableName}`
        (
            `_time` DateTime64(3) CODEC(DoubleDelta, ZSTD),
            `cluster_id` UUID,
            `cluster_name` LowCardinality(String),
            `uuid` UUID,
            `id` Int32,
            `user_name` LowCardinality(String),
            `app_id` LowCardinality(String),
            `host` String,
            `client_ip_address` String,
            `infobase_id` UUID,
            `connection_id` UUID,
            `process_id` UUID,
            `started_at` DateTime64(3),
            `last_active_at` DateTime64(3),
            `hibernate` UInt8,
            `passive_session_hibernate_time` Int32,
            `hibernate_session_terminate_time` Int32,
            `blocked_by_dbms` Int32,
            `blocked_by_ls` Int32,
            `bytes_all` Int64,
            `bytes_last5min` Int64,
            `calls_all` Int32,
            `calls_last5min` Int64,
            `dbms_bytes_all` Int64,
            `dbms_bytes_last5min` Int64,
            `db_proc_info` String,
            `db_proc_took` Int32,
            `db_proc_took_at` DateTime64(3),
            `duration_all` Int32,
            `duration_all_dbms` Int32,
            `duration_current` Int32,
            `duration_current_dbms` Int32,
            `duration_last_5_min` Int64,
            `duration_last_5_min_dbms` Int64,
            `memory_current` Int64,
            `memory_last5min` Int64,
            `memory_total` Int64,
            `read_current` Int64,
            `read_last5min` Int64,
            `read_total` Int64,
            `write_current` Int64,
            `write_last5min` Int64,
            `write_total` Int64,
            `duration_current_service` Int32,
            `duration_last5min_service` Int64,
            `duration_all_service` Int32,
            `current_service_name` LowCardinality(String),
            `cpu_time_current` Int64,
            `cpu_time_last5min` Int64,
            `cpu_time_total` Int64,
            `data_separation` String,
            `locale` LowCardinality(String),
            `licenses_count` UInt32
        )
        ENGINE = MergeTree()
        PARTITION BY toYYYYMM(_time)
        ORDER BY (_time, cluster_id, uuid);";

    public static string CreateProcessesTable(string databaseName) =>
        $@"CREATE TABLE IF NOT EXISTS `{databaseName}`.`{ProcessesTableName}`
        (
            `_time` DateTime64(3) CODEC(DoubleDelta, ZSTD),
            `cluster_id` UUID,
            `cluster_name` LowCardinality(String),
            `uuid` UUID,
            `host` LowCardinality(String),
            `port` UInt16,
            `pid` String,
            `running` UInt8,
            `enable` UInt8,
            `use` UInt8,
            `started_at` DateTime64(3),
            `connections` Int32,
            `capacity` Int32,
            `memory_size` Int32,
            `memory_excess_time` Int32,
            `selection_size` Int32,
            `available_performance` Int32,
            `avg_call_time` Float64,
            `avg_db_call_time` Float64,
            `avg_lock_call_time` Float64,
            `avg_server_call_time` Float64,
            `avg_back_call_time` Float64,
            `avg_threads` Float64,
            `licenses_count` UInt32
        )
        ENGINE = MergeTree()
        PARTITION BY toYYYYMM(_time)
        ORDER BY (_time, cluster_id, uuid);";

    public static string CreateConnectionsTable(string databaseName) =>
        $@"CREATE TABLE IF NOT EXISTS `{databaseName}`.`{ConnectionsTableName}`
        (
            `_time` DateTime64(3) CODEC(DoubleDelta, ZSTD),
            `cluster_id` UUID,
            `cluster_name` LowCardinality(String),
            `uuid` UUID,
            `id` Int32,
            `application` LowCardinality(String),
            `host` String,
            `connected_at` DateTime64(3),
            `infobase_id` UUID,
            `process_id` UUID,
            `session_id` Int32,
            `blocked_by_ls` Int32
        )
        ENGINE = MergeTree()
        PARTITION BY toYYYYMM(_time)
        ORDER BY (_time, cluster_id, uuid);";

    public static string CreateLocksTable(string databaseName) =>
        $@"CREATE TABLE IF NOT EXISTS `{databaseName}`.`{LocksTableName}`
        (
            `_time` DateTime64(3) CODEC(DoubleDelta, ZSTD),
            `cluster_id` UUID,
            `cluster_name` LowCardinality(String),
            `connection_id` UUID,
            `session_id` UUID,
            `object_id` UUID,
            `locked_at` DateTime64(3),
            `description` String
        )
        ENGINE = MergeTree()
        PARTITION BY toYYYYMM(_time)
        ORDER BY (_time, cluster_id, connection_id, object_id);";
}

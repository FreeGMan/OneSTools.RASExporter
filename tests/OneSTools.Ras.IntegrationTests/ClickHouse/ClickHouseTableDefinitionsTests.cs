using OneSTools.Ras.Exporter.Core.ClickHouse;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.ClickHouse;

public sealed class ClickHouseTableDefinitionsTests
{
    [Fact]
    public void Table_DDLs_Contain_Expected_Keywords_And_Engines()
    {
        string db = "test_cluster_db";

        string dbDdl = ClickHouseTableDefinitions.CreateDatabase(db);
        Assert.Contains("CREATE DATABASE IF NOT EXISTS `test_cluster_db`", dbDdl);

        string summaryDdl = ClickHouseTableDefinitions.CreateClusterSummaryTable(db);
        Assert.Contains("`test_cluster_db`.`cluster_summary`", summaryDdl);
        Assert.Contains("ENGINE = MergeTree()", summaryDdl);
        Assert.Contains("PARTITION BY toYYYYMM(_time)", summaryDdl);

        string sessionsDdl = ClickHouseTableDefinitions.CreateSessionsTable(db);
        Assert.Contains("`test_cluster_db`.`sessions`", sessionsDdl);
        Assert.Contains("`user_name` LowCardinality(String)", sessionsDdl);

        string processesDdl = ClickHouseTableDefinitions.CreateProcessesTable(db);
        Assert.Contains("`test_cluster_db`.`processes`", processesDdl);

        string connectionsDdl = ClickHouseTableDefinitions.CreateConnectionsTable(db);
        Assert.Contains("`test_cluster_db`.`connections`", connectionsDdl);

        string locksDdl = ClickHouseTableDefinitions.CreateLocksTable(db);
        Assert.Contains("`test_cluster_db`.`locks`", locksDdl);

        string infobasesDdl = ClickHouseTableDefinitions.CreateInfobasesTable(db);
        Assert.Contains("`test_cluster_db`.`infobases`", infobasesDdl);
        Assert.Contains("`infobase_id` UUID", infobasesDdl);
        Assert.Contains("`denied_from` Nullable(DateTime64(3))", infobasesDdl);
        Assert.Contains("ORDER BY (_time, cluster_id, infobase_id)", infobasesDdl);
        // Учётные данные СУБД не должны попадать в аналитическое хранилище.
        Assert.DoesNotContain("db_pwd", infobasesDdl);
        Assert.DoesNotContain("external_session_manager_connection_string", infobasesDdl);
    }
}

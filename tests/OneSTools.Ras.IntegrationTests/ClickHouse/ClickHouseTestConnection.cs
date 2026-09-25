namespace OneSTools.Ras.IntegrationTests.ClickHouse;

internal static class ClickHouseTestConnection
{
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKHOUSE_TEST_CONNECTION_STRING")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKHOUSE_PASSWORD"));

    public static string ConnectionString => Environment.GetEnvironmentVariable("CLICKHOUSE_TEST_CONNECTION_STRING") ??
        $"Host=clickhouse;Port=8123;Username=default;Password={Environment.GetEnvironmentVariable("CLICKHOUSE_PASSWORD")};";
}

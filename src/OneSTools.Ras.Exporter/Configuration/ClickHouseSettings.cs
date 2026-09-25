namespace OneSTools.Ras.Exporter.Configuration;

/// <summary>Секция "ClickHouse" — параметры хранилища.</summary>
public sealed class ClickHouseSettings
{
    public const string SectionName = "ClickHouse";

    /// <summary>Строка подключения ClickHouse (HTTP интерфейс, порт по умолчанию 8123).</summary>
    public string ConnectionString { get; set; } = "Host=clickhouse;Port=8123;Username=default;";

    /// <summary>
    /// Шаблон имени базы данных кластера. Поддерживает подстановки:
    /// {ClusterName} и {ClusterId}. По умолчанию: ras_cluster_{ClusterName}.
    /// </summary>
    public string DatabaseTemplate { get; set; } = "ras_cluster_{ClusterName}";

    public bool AutoCreateDatabases { get; set; } = true;

    public bool AutoCreateTables { get; set; } = true;

    /// <summary>Размер порции записи (строк), отправляемой за одну Bulk-операцию.</summary>
    public int BatchSize { get; set; } = 10000;

    /// <summary>Максимальное время накопления перед принудительным сбросом порции (сек).</summary>
    public int FlushIntervalSeconds { get; set; } = 5;

}

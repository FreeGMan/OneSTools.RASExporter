namespace OneSTools.Ras.Exporter.Configuration;

/// <summary>Секция "RasExporter" — список серверов RAS для опроса.</summary>
public sealed class RasExporterSettings
{
    public const string SectionName = "RasExporter";

    public List<RasServerSettings> Servers { get; set; } = new();
}

/// <summary>Сервер RAS (агент администрирования) и настройки его опроса.</summary>
public sealed class RasServerSettings
{
    /// <summary>Логическое имя сервера (для логов и именования).</summary>
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1545;

    public int ConnectTimeoutMilliseconds { get; set; } = 5000;

    /// <summary>Авторизация администратора агента (уровень сервера).</summary>
    public AgentAuthSettings AgentAuth { get; set; } = new();

    /// <summary>Частота опроса каждой группы метрик.</summary>
    public PollIntervalsSettings PollIntervals { get; set; } = new();

    /// <summary>Кластеры, обслуживаемые данным сервером.</summary>
    public List<RasClusterSettings> Clusters { get; set; } = new();
}

/// <summary>Учётные данные администратора агента (уровень сервера RAS).</summary>
public sealed class AgentAuthSettings
{
    public string? User { get; set; }

    public string? Password { get; set; }
}

/// <summary>Частота опроса метрик в секундах.</summary>
public sealed class PollIntervalsSettings
{
    public int ClustersSeconds { get; set; } = 300;
    public int ProcessesSeconds { get; set; } = 5;
    public int SessionsSeconds { get; set; } = 5;
    public int ConnectionsSeconds { get; set; } = 10;
    public int LocksSeconds { get; set; } = 5;
    public int InfoBasesSeconds { get; set; } = 300;

    public TimeSpan Clusters => TimeSpan.FromSeconds(ClustersSeconds);
    public TimeSpan Processes => TimeSpan.FromSeconds(ProcessesSeconds);
    public TimeSpan Sessions => TimeSpan.FromSeconds(SessionsSeconds);
    public TimeSpan Connections => TimeSpan.FromSeconds(ConnectionsSeconds);
    public TimeSpan Locks => TimeSpan.FromSeconds(LocksSeconds);
    public TimeSpan InfoBases => TimeSpan.FromSeconds(InfoBasesSeconds);
}

/// <summary>Конкретный кластер 1С на сервере RAS.</summary>
public sealed class RasClusterSettings
{
    /// <summary>UUID кластера (может быть определён автоматически по имени при первом опросе).</summary>
    public Guid? ClusterId { get; set; }

    public string ClusterName { get; set; } = string.Empty;

    public string? User { get; set; }

    public string? Password { get; set; }

    /// <summary>Включает/отключает сбор метрик по кластеру без удаления из конфигурации.</summary>
    public bool Enabled { get; set; } = true;
}
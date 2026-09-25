using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSTools.Ras.Exporter.Configuration;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;
using OneSTools.Ras.Exporter.Services;
using OneSTools.Ras.Exporter.Services.Health;

// Content root = каталог исполняемого файла: appsettings.json всегда подхватывается
// рядом с бинарником (publish) и из выходного каталога (dotnet run из любой директории).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// appsettings.local.json (gitignored) — локальные секреты/переопределения. Хост добавляет
// env/cmd-источники дважды (bootstrap и основные); вставляем local перед ПОСЛЕДНИМ env/cmd,
// чтобы приоритет был: base-appsettings < local < user-secrets < env < args.
{
    var configSources = builder.Configuration.Sources;
    int lastEnvCmd = -1;
    for (int i = 0; i < configSources.Count; i++)
    {
        string sourceName = configSources[i].GetType().Name;
        if (sourceName is "EnvironmentVariablesConfigurationSource" or "CommandLineConfigurationSource")
            lastEnvCmd = i;
    }

    int before = configSources.Count;
    builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

    if (lastEnvCmd >= 0 && lastEnvCmd < before)
    {
        var localSource = (IConfigurationSource)configSources[before];
        configSources.RemoveAt(before);
        configSources.Insert(lastEnvCmd, localSource);
    }
}

builder.Services
    .AddOptions<RasExporterSettings>()
    .Bind(builder.Configuration.GetSection(RasExporterSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RasExporterSettings>, RasExporterSettingsValidator>();

builder.Services
    .AddOptions<ClickHouseSettings>()
    .Bind(builder.Configuration.GetSection(ClickHouseSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ClickHouseSettings>, ClickHouseSettingsValidator>();

builder.Services.AddSingleton<IMetricsStorage>(sp =>
{
    var chSettings = sp.GetRequiredService<IOptions<ClickHouseSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<ClickHouseMetricsStorage>>();
    return new ClickHouseMetricsStorage(
        chSettings.ConnectionString,
        chSettings.DatabaseTemplate,
        chSettings.AutoCreateDatabases,
        chSettings.AutoCreateTables,
        chSettings.BatchSize,
        logger);
});

// Гейт доступности хранилища: при падении ClickHouse опросы RAS приостанавливаются,
// восстановление проверяется зондом ProbeAsync.
builder.Services.AddSingleton(sp =>
{
    var storage = sp.GetRequiredService<IMetricsStorage>();
    var logger = sp.GetRequiredService<ILogger<StorageHealthGate>>();
    return new StorageHealthGate(ct => storage.ProbeAsync(ct), TimeSpan.FromSeconds(30), logger);
});

builder.Services.AddSingleton(sp =>
{
    var storage = sp.GetRequiredService<IMetricsStorage>();
    var chSettings = sp.GetRequiredService<IOptions<ClickHouseSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<MetricsBatchingPipeline>>();
    var gate = sp.GetRequiredService<StorageHealthGate>();
    return new MetricsBatchingPipeline(
        storage,
        chSettings.BatchSize,
        TimeSpan.FromSeconds(chSettings.FlushIntervalSeconds),
        logger,
        gate);
});

builder.Services.AddSingleton<RasExporterHealthRegistry>();

builder.Services.AddHealthChecks()
    .AddCheck<RasExporterHealthCheck>("ras-cluster-sessions");

builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Delay = TimeSpan.FromSeconds(10);
    options.Period = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IHealthCheckPublisher, LoggingHealthCheckPublisher>();

builder.Services.AddHostedService<RasExportService>();

var host = builder.Build();
await host.RunAsync();

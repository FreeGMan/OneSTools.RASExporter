using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSTools.Ras.Exporter.Configuration;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Core.Storage;
using OneSTools.Ras.Exporter.Services;

// Content root = каталог исполняемого файла: appsettings.json всегда подхватывается
// рядом с бинарником (publish) и из выходного каталога (dotnet run из любой директории).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services
    .AddOptions<RasExporterSettings>()
    .Bind(builder.Configuration.GetSection(RasExporterSettings.SectionName))
    .ValidateOnStart();

builder.Services
    .AddOptions<ClickHouseSettings>()
    .Bind(builder.Configuration.GetSection(ClickHouseSettings.SectionName))
    .ValidateOnStart();

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

builder.Services.AddSingleton(sp =>
{
    var storage = sp.GetRequiredService<IMetricsStorage>();
    var chSettings = sp.GetRequiredService<IOptions<ClickHouseSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<MetricsBatchingPipeline>>();
    return new MetricsBatchingPipeline(
        storage,
        chSettings.BatchSize,
        TimeSpan.FromSeconds(chSettings.FlushIntervalSeconds),
        logger);
});

builder.Services.AddHostedService<RasExportService>();

var host = builder.Build();
await host.RunAsync();

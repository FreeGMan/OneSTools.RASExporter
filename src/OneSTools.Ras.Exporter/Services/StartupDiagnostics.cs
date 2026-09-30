using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSTools.Ras.Exporter.Configuration;
using OneSTools.Ras.Exporter.Services.Logging;

namespace OneSTools.Ras.Exporter.Services;

/// <summary>
/// Диагностика конфигурации на старте: каталог поиска, что найдено, и разбор
/// <see cref="OptionsValidationException"/> в stderr без стек-трейса.
/// </summary>
internal sealed class StartupDiagnostics
{
    internal const string Category = "OneSTools.Ras.Exporter.Startup";

    private readonly IConfiguration _configuration;
    private readonly LogFormat _format;

    internal StartupDiagnostics(IConfiguration configuration)
    {
        _configuration = configuration;
        _format = LogFormat.From(configuration);
    }

    internal static string BaseConfigPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    internal static string LocalConfigPath => Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

    internal void LogConfigurationSources(ILogger logger)
    {
        int servers = _configuration.GetSection(RasExporterSettings.SectionName)
            .GetSection("Servers").GetChildren().Count();

        logger.LogInformation(
            "Конфигурация: каталог {Directory}; appsettings.json {Base}; appsettings.local.json {Local}; серверов RAS: {Servers}",
            AppContext.BaseDirectory,
            File.Exists(BaseConfigPath) ? "найден" : "НЕ найден",
            File.Exists(LocalConfigPath) ? "найден" : "не найден",
            servers);
    }

    internal void ReportValidationFailure(OptionsValidationException exception)
    {
        List<string> failures = exception.Failures.ToList();
        if (failures.Count == 0)
            failures.Add(exception.Message);

        Console.Error.WriteLine(Prefix(LogLevel.Error) + " Конфигурация некорректна:");
        foreach (string failure in failures)
            Console.Error.WriteLine(LogFormat.Indent + "- " + failure);

        Console.Error.WriteLine(Prefix(LogLevel.Error) +
            $" Конфигурация читается из каталога {AppContext.BaseDirectory}: appsettings.json " +
            (File.Exists(BaseConfigPath) ? "найден" : "НЕ найден") +
            " (шаблон — appsettings.production.example.json рядом с бинарником), " +
            "далее appsettings.local.json, переменные окружения и аргументы командной строки.");
        Console.Error.WriteLine(Prefix(LogLevel.Error) +
            " Пример env-переопределений: RasExporter__Servers__0__Name=ras-prod, " +
            "RasExporter__Servers__0__Host=<ras-host>, RasExporter__Servers__0__Port=1545, " +
            "ClickHouse__ConnectionString=Host=<ch>;Port=8123;Username=<user>;Password=<pwd>");
    }

    private string Prefix(LogLevel level) => _format.Prefix(Category, level);
}

using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OneSTools.Ras.Exporter.Services.Logging;

/// <summary>
/// Параметры формата журнала, читаемые из секции <c>Logging:Console:FormatterOptions:onestools</c>.
/// Применяются и к консольному форматтеру, и к прямым записям в stderr (ошибки конфигурации),
/// чтобы весь вывод одного запуска был в одном формате.
/// </summary>
internal sealed class LogFormat
{
    internal const string SectionName = "Logging:Console:FormatterOptions:onestools";
    internal const string DefaultTimestampFormat = "dd.MM.yyyy HH:mm:ss";
    internal const string Indent = "          ";

    private const string TimestampFormatKey = "TimestampFormat";
    private const string UseUtcTimestampKey = "UseUtcTimestamp";

    private LogFormat(string timestampFormat, bool useUtcTimestamp)
    {
        TimestampFormat = timestampFormat;
        UseUtcTimestamp = useUtcTimestamp;
    }

    internal string TimestampFormat { get; }
    internal bool UseUtcTimestamp { get; }

    internal static LogFormat From(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);

        string? timestampFormat = section[TimestampFormatKey];
        bool.TryParse(section[UseUtcTimestampKey], out bool useUtcTimestamp);
        return new LogFormat(string.IsNullOrWhiteSpace(timestampFormat) ? DefaultTimestampFormat : timestampFormat, useUtcTimestamp);
    }

    internal string Prefix(string category, LogLevel level)
    {
        DateTimeOffset timestamp = UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
        return $"{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)} [{LevelToken(level)}] [{category}]";
    }

    internal static string LevelToken(LogLevel level) => level switch
    {
        LogLevel.Trace => "trc",
        LogLevel.Debug => "dbg",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "err",
        LogLevel.Critical => "crit",
        _ => "none",
    };
}

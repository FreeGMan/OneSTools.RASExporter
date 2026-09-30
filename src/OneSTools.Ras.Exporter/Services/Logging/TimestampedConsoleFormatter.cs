using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace OneSTools.Ras.Exporter.Services.Logging;

/// <summary>
/// Однострочный формат журнала службы:
/// <c>30.09.2026 11:38:00 [info] [Cluster.MainCluster] Текст сообщения</c>.
/// Исключение — следующими строками с отступом. ANSI-последовательности не эмитируются,
/// чтобы перенаправление вывода в файл журнала не превращалось в мусор.
/// </summary>
public sealed class TimestampedConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "onestools";
    public const string DefaultTimestampFormat = LogFormat.DefaultTimestampFormat;

    private readonly LogFormat _format;

    public TimestampedConsoleFormatter(IConfiguration configuration)
        : base(FormatterName)
    {
        _format = LogFormat.From(configuration);
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
            return;

        textWriter.Write(_format.Prefix(logEntry.Category, logEntry.LogLevel));
        textWriter.Write(' ');
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            foreach (string line in logEntry.Exception.ToString().Split(Environment.NewLine))
                textWriter.WriteLine(LogFormat.Indent + line);
        }
    }
}

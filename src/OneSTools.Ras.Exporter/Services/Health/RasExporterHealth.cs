using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OneSTools.Ras.Exporter.Core.Pipeline;

namespace OneSTools.Ras.Exporter.Services.Health;

public sealed record RasClusterHealth(string Key, bool Healthy, DateTimeOffset? LastConnectedAt, string? LastError);

/// <summary>
/// Потокобезопасный реестр здоровья (сервер/кластер)-сессий. Обновляется циклами
/// RasExportService; наружу отдаётся через <see cref="RasExporterHealthCheck"/>.
/// </summary>
public sealed class RasExporterHealthRegistry
{
    private readonly ConcurrentDictionary<string, RasClusterHealth> _states = new();

    public void MarkConnected(string key) =>
        _states[key] = new RasClusterHealth(key, true, DateTimeOffset.UtcNow, null);

    public void MarkFailed(string key, string error) =>
        _states.AddOrUpdate(
            key,
            _ => new RasClusterHealth(key, false, null, error),
            (_, old) => old with { Healthy = false, LastError = error });

    public IReadOnlyCollection<RasClusterHealth> Snapshot() => _states.Values.ToArray();
}

public sealed class RasExporterHealthCheck(RasExporterHealthRegistry registry, StorageHealthGate gate) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!gate.IsAvailable)
            return Task.FromResult(HealthCheckResult.Degraded("ClickHouse недоступен — опрос RAS приостановлен, ожидание восстановления."));

        var states = registry.Snapshot();
        if (states.Count == 0)
            return Task.FromResult(HealthCheckResult.Degraded("Сбор ещё не запускался (нет активных кластеров)."));

        var unhealthy = states.Where(s => !s.Healthy).ToList();
        if (unhealthy.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy($"{states.Count} кластерных сессий активно."));

        var degraded = unhealthy.Count < states.Count;
        var description = string.Join("; ", unhealthy.Select(s => $"{s.Key}: {s.LastError}"));
        return Task.FromResult(degraded
            ? HealthCheckResult.Degraded($"{unhealthy.Count}/{states.Count} сессий нездоровы: {description}")
            : HealthCheckResult.Unhealthy($"все сессий нездоровы: {description}"));
    }
}

/// <summary>
/// Пишет в лог только ПЕРЕХОДЫ состояния (шум подавляется) — способ наблюдения здоровья
/// worker-процесса без HTTP-эндпоинта.
/// </summary>
public sealed class LoggingHealthCheckPublisher(ILogger<LoggingHealthCheckPublisher> logger) : IHealthCheckPublisher
{
    private volatile string _lastStatus = string.Empty;

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        string status = $"{report.Status}: {report.TotalDuration.TotalMilliseconds:F0} ms";
        if (report.Status != HealthStatus.Healthy)
            status += " | " + string.Join("; ", report.Entries.Where(e => e.Value.Status != HealthStatus.Healthy)
                .Select(e => $"{e.Key}={e.Value.Status} ({e.Value.Description})"));

        if (status != _lastStatus)
        {
            _lastStatus = status;
            switch (report.Status)
            {
                case HealthStatus.Healthy: logger.LogInformation("Health: {Status}", status); break;
                case HealthStatus.Degraded: logger.LogWarning("Health: {Status}", status); break;
                default: logger.LogError("Health: {Status}", status); break;
            }
        }
        return Task.CompletedTask;
    }
}

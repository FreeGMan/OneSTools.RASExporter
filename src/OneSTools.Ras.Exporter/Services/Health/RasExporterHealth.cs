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

/// <summary>Снимок здоровья одной кластерной сессии.</summary>
public sealed record RasClusterHealth(string Key, bool Healthy, DateTimeOffset? LastConnectedAt, string? LastError);

/// <summary>Состояние периодического опроса одного типа метрик.</summary>
public sealed record RasPollHealth(string ClusterKey, string Collector, TimeSpan Interval, DateTimeOffset StartedAt,
    DateTimeOffset? LastSuccessAt, int ConsecutiveFailures, string? LastError);

/// <summary>
/// Потокобезопасный реестр здоровья (сервер/кластер)-сессий. Обновляется циклами
/// RasExportService; наружу отдаётся через <see cref="RasExporterHealthCheck"/>.
/// </summary>
public sealed class RasExporterHealthRegistry
{
    private readonly ConcurrentDictionary<string, RasClusterHealth> _states = new();
    private readonly ConcurrentDictionary<(string ClusterKey, string Collector), RasPollHealth> _polls = new();

    public void MarkConnected(string key) =>
        _states[key] = new RasClusterHealth(key, true, DateTimeOffset.UtcNow, null);

    public void MarkFailed(string key, string error) =>
        _states.AddOrUpdate(
            key,
            _ => new RasClusterHealth(key, false, null, error),
            (_, old) => old with { Healthy = false, LastError = error });

    public IReadOnlyCollection<RasClusterHealth> Snapshot() => _states.Values.ToArray();

    public IReadOnlyCollection<RasPollHealth> PollSnapshot() => _polls.Values.ToArray();

    public void ConfigurePolls(string key, IReadOnlyDictionary<string, TimeSpan> intervals)
    {
        foreach (var pollKey in _polls.Keys.Where(k => k.ClusterKey == key))
            _polls.TryRemove(pollKey, out _);

        foreach (var (collector, interval) in intervals)
            _polls[(key, collector)] = new RasPollHealth(key, collector, interval, DateTimeOffset.UtcNow, null, 0, null);
    }

    public void RecordPoll(string key, string collector, Exception? error)
    {
        var pollKey = (key, collector);
        while (_polls.TryGetValue(pollKey, out var previous))
        {
            var next = error is null
                ? previous with { LastSuccessAt = DateTimeOffset.UtcNow, ConsecutiveFailures = 0, LastError = null }
                : previous with { ConsecutiveFailures = previous.ConsecutiveFailures + 1, LastError = error.Message };
            if (_polls.TryUpdate(pollKey, next, previous))
                return;
        }
    }

    public void Remove(string key)
    {
        _states.TryRemove(key, out _);
        foreach (var pollKey in _polls.Keys.Where(k => k.ClusterKey == key))
            _polls.TryRemove(pollKey, out _);
    }
}

/// <summary>Health-check: состояние гейта хранилища + RAS-сессий (docs/SECURITY.md §3).</summary>
public sealed class RasExporterHealthCheck(RasExporterHealthRegistry registry, StorageHealthGate gate,
    MetricsBatchingPipeline pipeline) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!gate.IsAvailable)
            return Task.FromResult(HealthCheckResult.Degraded("ClickHouse недоступен — опрос RAS приостановлен, ожидание восстановления."));

        var states = registry.Snapshot();
        if (states.Count == 0)
            return Task.FromResult(HealthCheckResult.Degraded("Сбор ещё не запускался (нет активных кластеров)."));

        var unhealthy = states.Where(s => !s.Healthy).ToList();
        if (unhealthy.Count > 0)
        {
            var description = string.Join("; ", unhealthy.Select(s => $"{s.Key}: {s.LastError}"));
            return Task.FromResult(unhealthy.Count < states.Count
                ? HealthCheckResult.Degraded($"{unhealthy.Count}/{states.Count} сессий нездоровы: {description}")
                : HealthCheckResult.Unhealthy($"Все сессии нездоровы: {description}"));
        }

        var polls = registry.PollSnapshot();
        if (polls.Count == 0)
            return Task.FromResult(HealthCheckResult.Degraded("Ожидается инициализация схемы и запуск опросов."));

        var now = DateTimeOffset.UtcNow;
        var failed = polls.Where(p => p.LastError is not null ||
            now - (p.LastSuccessAt ?? p.StartedAt) > TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(30).Ticks, p.Interval.Ticks * 2)))
            .ToList();
        if (failed.Count > 0)
        {
            var description = string.Join("; ", failed.Select(p => $"{p.ClusterKey}/{p.Collector}: {p.LastError ?? "нет свежего опроса"}"));
            return Task.FromResult(HealthCheckResult.Degraded($"Опросы с ошибкой ({failed.Count}/{polls.Count}): {description}"));
        }

        if (pipeline.DroppedItems > 0)
            return Task.FromResult(HealthCheckResult.Degraded($"Потеряно элементов метрик: {pipeline.DroppedItems}."));

        return Task.FromResult(HealthCheckResult.Healthy($"{states.Count} кластерных сессий активно."));
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
        string status = report.Status.ToString();
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

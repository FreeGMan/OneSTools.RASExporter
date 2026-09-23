using System;
using System.Threading;

namespace OneSTools.Ras.Exporter.Core.Pipeline;

/// <summary>
/// Экспоненциальный backoff с джиттером для циклов переподключения (TODO.md T2/R3):
/// 1s → 2s → 5s → 10s → 30s → 60s (потолок), каждый шаг ±20% джиттера, сброс после успеха.
/// </summary>
public sealed class BackoffPolicy
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    private int _attempt;

    public void Reset() => Interlocked.Exchange(ref _attempt, 0);

    public TimeSpan NextDelay()
    {
        int attempt = Interlocked.Increment(ref _attempt) - 1;
        TimeSpan baseDelay = Steps[Math.Min(attempt, Steps.Length - 1)];
        double jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * jitter);
    }
}

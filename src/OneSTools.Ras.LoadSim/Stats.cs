using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OneSTools.Ras.LoadSim;

/// <summary>Агрегатор выборок: латентность (p50/p95/max) + ошибки по типам.</summary>
public sealed class SampleSet(string name)
{
    private readonly ConcurrentQueue<double> _latenciesMs = new();
    private readonly ConcurrentDictionary<string, int> _errors = new();
    private long _items;

    public string Name { get; } = name;

    public void RecordLatency(double ms) => _latenciesMs.Enqueue(ms);

    public void RecordItems(long count) => System.Threading.Interlocked.Add(ref _items, count);

    public void RecordError(Exception ex)
    {
        var key = ex.GetType().Name + ": " + Truncate(ex.Message.Replace('\n', ' '), 160);
        _errors.AddOrUpdate(key, 1, (_, c) => c + 1);
    }

    public void RecordError(string key)
    {
        _errors.AddOrUpdate(Truncate(key, 160), 1, (_, c) => c + 1);
    }

    public SampleSummary Summarize()
    {
        var l = _latenciesMs.ToArray();
        Array.Sort(l);
        return new SampleSummary(
            Name,
            l.Length,
            l.Length == 0 ? 0 : Percentile(l, 0.50),
            l.Length == 0 ? 0 : Percentile(l, 0.95),
            l.Length == 0 ? 0 : l[^1],
            System.Math.Round(l.Sum(), 1),
            System.Threading.Interlocked.Read(ref _items),
            new Dictionary<string, int>(_errors));
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>Итог агрегата для отчёта.</summary>
public sealed record SampleSummary(
    string Name,
    int Count,
    double P50Ms,
    double P95Ms,
    double MaxMs,
    double TotalMs,
    long Items,
    Dictionary<string, int> Errors)
{
    public bool HasErrors => Errors.Count > 0;

    public string Format()
    {
        var errs = HasErrors ? "  ERR[" + string.Join("; ", Errors.Select(e => e.Value + "× " + e.Key)) + "]" : string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0,-28} n={1,-5} p50={2,8:F1} p95={3,8:F1} max={4,8:F1} items={5}{6}",
            Name, Count, P50Ms, P95Ms, MaxMs, Items, errs);
    }
}

/// <summary>Одна фаза нагрузочного прогона в отчёте.</summary>
public sealed record PhaseResult(string Phase, List<SampleSummary> Summary, Dictionary<string, string> Notes);

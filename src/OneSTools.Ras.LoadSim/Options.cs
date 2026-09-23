using System;
using System.Collections.Generic;
using System.Linq;

namespace OneSTools.Ras.LoadSim;

/// <summary>Параметры прогона LoadSim (CLI --flag value).</summary>
public sealed record Options
{
    public string Host { get; init; } = Environment.GetEnvironmentVariable("RAC_TEST_RAS_HOST") ?? "1c-test-server";
    public int Port { get; init; } = 1545;
    public string Mode { get; init; } = "all";           // all | stage1 | ramp | churn | negotiation | chaos
    public int Clients { get; init; } = 3;               // stage1: «кластеров» (соединений)
    public int DurationSeconds { get; init; } = 120;     // stage1: длительность
    public int Speedup { get; init; } = 2;               // сжатие интервалов коллекторов
    public int[] Levels { get; init; } = [3, 10, 25, 50];// ramp:Concurrency levels
    public int HoldSeconds { get; init; } = 45;          // ramp: удержание уровня
    public int ChurnIterations { get; init; } = 100;     // churn: число обрывов
    public int ChurnWorkers { get; init; } = 4;
    public int SanitySeconds { get; init; } = 30;        // churn: пост-проверка
    public int NegotiationClients { get; init; } = 20;
    public string ReportDirectory { get; init; } = AppContext.BaseDirectory;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                overrides[args[i][2..]] = args[i + 1];
        }

        int GetInt(string key, int current) => overrides.TryGetValue(key, out var v) && int.TryParse(v, out var p) ? p : current;

        o = o with
        {
            Host = overrides.GetValueOrDefault("host", o.Host),
            Port = GetInt("port", o.Port),
            Mode = overrides.GetValueOrDefault("mode", o.Mode),
            Clients = GetInt("clients", o.Clients),
            DurationSeconds = GetInt("duration", o.DurationSeconds),
            Speedup = GetInt("speedup", o.Speedup),
            HoldSeconds = GetInt("hold", o.HoldSeconds),
            ChurnIterations = GetInt("churn", o.ChurnIterations),
            ChurnWorkers = GetInt("churn-workers", o.ChurnWorkers),
            SanitySeconds = GetInt("sanity", o.SanitySeconds),
            NegotiationClients = GetInt("negotiation-clients", o.NegotiationClients),
            ReportDirectory = overrides.GetValueOrDefault("report-dir", o.ReportDirectory),
        };

        if (overrides.TryGetValue("levels", out var lv))
            o = o with { Levels = lv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray() };

        return o;
    }
}

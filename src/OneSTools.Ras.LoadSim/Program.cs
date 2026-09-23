using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OneSTools.Ras.LoadSim;

var options = Options.Parse(args);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"OneSTools.Ras.LoadSim -> {options.Host}:{options.Port} mode={options.Mode} clients={options.Clients} " +
    $"duration={options.DurationSeconds}s speedup={options.Speedup} levels=[{string.Join(',', options.Levels)}] " +
    $"hold={options.HoldSeconds}s churn={options.ChurnIterations}w{options.ChurnWorkers} neg-clients={options.NegotiationClients}");

var scenarios = new Scenarios(options);
var phases = new List<PhaseResult>();
Guid clusterId;

try
{
    clusterId = await scenarios.PreflightAsync(cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Preflight FAILED: {ex.GetType().Name}: {ex.Message}");
    return 2;
}

var started = DateTimeOffset.UtcNow;

async Task RunPhase(string name, Func<Task<PhaseResult>> run)
{
    if (cts.IsCancellationRequested) return;
    Console.WriteLine($"\n=== {name} ===");
    try
    {
        var result = await run();
        phases.Add(result);
        PrintPhase(result);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"{name}: cancelled");
    }
    catch (Exception ex)
    {
        phases.Add(new PhaseResult(name, [], new Dictionary<string, string> { ["fatal"] = $"{ex.GetType().Name}: {ex.Message}" }));
        Console.Error.WriteLine($"{name}: FATAL {ex}");
    }
}

var mode = options.Mode.ToLowerInvariant();
if (mode is "all" or "stage1")
    await RunPhase("stage1", () => scenarios.Stage1MonitoringSimulationAsync(clusterId, cts.Token));
if (mode is "all" or "ramp")
    await RunPhase("ramp", () => scenarios.RampConnectionsAsync(clusterId, cts.Token));
if (mode is "all" or "churn")
    await RunPhase("churn", () => scenarios.ChurnSocketKillAsync(cts.Token));
if (mode == "chaos")
    await RunPhase("chaos", () => scenarios.ChaosMixedAsync(clusterId, cts.Token));
if (mode is "all" or "negotiation")
    await RunPhase("negotiation", () => scenarios.NegotiationStormAsync(cts.Token));

var ended = DateTimeOffset.UtcNow;
var report = new
{
    started,
    ended,
    durationSeconds = (ended - started).TotalSeconds,
    options = new { options.Host, options.Port, options.Mode, options.Clients, options.DurationSeconds, options.Speedup, levels = options.Levels, options.HoldSeconds, options.ChurnIterations, options.NegotiationClients },
    clusterId,
    phases
};

var path = Path.Combine(options.ReportDirectory, $"loadsim-{mode}-{started:yyyyMMdd-HHmmss}.json");
try
{
    File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"\nReport: {path}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"report write failed: {ex.Message}");
}

int failures = phases.Sum(p => p.Summary.Where(s => s.HasErrors).Sum(s => s.Errors.Values.Sum()) + p.Notes.Count(n => n.Key == "fatal"));
Console.WriteLine(failures == 0 ? "\nRESULT: OK (no unexpected errors)" : $"\nRESULT: {failures} error sample(s) — see report");
return failures == 0 ? 0 : 1;

static void PrintPhase(PhaseResult phase)
{
    Console.WriteLine($"--- {phase.Phase} ---");
    foreach (var s in phase.Summary)
        Console.WriteLine("  " + s.Format());
    foreach (var (k, v) in phase.Notes)
        Console.WriteLine($"  note {k}: {v}");
}

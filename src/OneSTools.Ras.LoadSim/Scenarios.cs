using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Client;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Transport;

namespace OneSTools.Ras.LoadSim;

/// <summary>Наборы нагрузочных сценариев (stage1/ramp/churn/negotiation/chaos).</summary>
public sealed class Scenarios(Options options)
{
    private const int AdminProtoVersion = 14;
    private static readonly ServiceId AdminService = new(RasAdminClient.AdminServiceName, AdminProtoVersion + ".0");

    private readonly Options _options = options;

    public async Task<PhaseResult> Stage1MonitoringSimulationAsync(Guid clusterId, CancellationToken ct)
    {
        var sets = NewStage1Sets();
        var notes = new Dictionary<string, string>
        {
            ["clients"] = _options.Clients.ToString(),
            ["durationSeconds"] = _options.DurationSeconds.ToString(),
            ["speedup"] = _options.Speedup.ToString()
        };

        using var phase = new CancellationTokenSource(TimeSpan.FromSeconds(_options.DurationSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, phase.Token);

        var workers = Enumerable.Range(1, _options.Clients)
            .Select(i => MonitoringClientLoopAsync($"client{i}", clusterId, sets, linked.Token))
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
        }

        return Finalize("stage1-monitoring", sets, notes);
    }

    private static Dictionary<string, SampleSet> NewStage1Sets() => new()
    {
        ["connect+negotiate"] = new("connect+negotiate"),
        ["authenticateCluster"] = new("authenticateCluster"),
        ["getSessions"] = new("getSessions"),
        ["getWorkingProcesses"] = new("getWorkingProcesses"),
        ["getConnectionsShort"] = new("getConnectionsShort"),
        ["getLocks"] = new("getLocks"),
        ["getClusterInfo"] = new("getClusterInfo"),
        ["getInfoBases"] = new("getInfoBases")
    };

    private async Task MonitoringClientLoopAsync(string label, Guid clusterId, Dictionary<string, SampleSet> sets, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var client = new RasAdminClient(_options.Host, _options.Port, NullLogger<RasAdminClient>.Instance);

                var sw = Stopwatch.StartNew();
                await client.ConnectAsync(cancellationToken: ct);
                sets["connect+negotiate"].RecordLatency(sw.Elapsed.TotalMilliseconds);

                sw.Restart();
                await client.AuthenticateClusterAsync(clusterId, "", "", ct);
                sets["authenticateCluster"].RecordLatency(sw.Elapsed.TotalMilliseconds);

                var nextDue = new Dictionary<string, DateTime>();
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    foreach (var (metric, interval, action) in Stage1Queries(client, clusterId, ct))
                    {
                        if (nextDue.TryGetValue(metric, out var due) && due > DateTime.UtcNow)
                            continue;

                        sw.Restart();
                        long items = await action();
                        sets[metric].RecordLatency(sw.Elapsed.TotalMilliseconds);
                        sets[metric].RecordItems(items);
                        nextDue[metric] = DateTime.UtcNow.AddSeconds(interval / Math.Max(1, _options.Speedup));
                    }

                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                sets["connect+negotiate"].RecordError(ex);
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
        }
    }

    private static IEnumerable<(string Metric, double IntervalSeconds, Func<Task<long>> Action)> Stage1Queries(
        RasAdminClient client, Guid clusterId, CancellationToken ct)
    {
        yield return ("getSessions", 5, async () => (await client.GetSessionsAsync(clusterId, ct)).Count);
        yield return ("getWorkingProcesses", 5, async () => (await client.GetWorkingProcessesAsync(clusterId, ct)).Count);
        yield return ("getLocks", 5, async () => (await client.GetLocksAsync(clusterId, ct)).Count);
        yield return ("getConnectionsShort", 10, async () => (await client.GetConnectionsShortAsync(clusterId, ct)).Count);
        yield return ("getClusterInfo", 300, async () => { await client.GetClusterInfoAsync(clusterId, ct); return 1; });
        yield return ("getInfoBases", 300, async () => (await client.GetInfoBasesAsync(clusterId, ct)).Count);
    }

    public async Task<PhaseResult> RampConnectionsAsync(Guid clusterId, CancellationToken ct)
    {
        var allSummaries = new List<SampleSummary>();
        var notes = new Dictionary<string, string>();

        foreach (var level in _options.Levels)
        {
            Console.WriteLine($"  ramp level={level} ...");
            var sets = new Dictionary<string, SampleSet>
            {
                ["connect"] = new($"connect@{level}"),
                ["open"] = new($"openEndpoint@{level}"),
                ["authenticate"] = new($"authenticate@{level}"),
                ["getSessions"] = new($"getSessions@{level}")
            };

            using var levelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            levelCts.CancelAfter(TimeSpan.FromSeconds(_options.HoldSeconds));

            var clients = Enumerable.Range(0, level)
                .Select(_ => RampClientAsync(sets, clusterId, levelCts.Token))
                .ToArray();

            try
            {
                await Task.WhenAll(clients);
            }
            catch (OperationCanceledException)
            {
            }

            allSummaries.AddRange(sets.Values.Select(s => s.Summarize()));
            notes[$"level{level}"] = string.Join("; ", sets.Values
                .Select(s => s.Summarize())
                .Where(s => s.HasErrors).Select(s => $"{s.Name}:{s.Errors.Values.Sum()}"));
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return new PhaseResult("ramp-connections", allSummaries, notes);
    }

    private async Task RampClientAsync(Dictionary<string, SampleSet> sets, Guid clusterId, CancellationToken ct)
    {
        var sw = new Stopwatch();
        try
        {
            await using var conn = new RasConnection(new DnsEndPoint(_options.Host, _options.Port));

            sw.Restart();
            await conn.ConnectAsync(new Dictionary<string, object?> { ["client.name"] = "loadsim-ramp" }, ct);
            sets["connect"].RecordLatency(sw.Elapsed.TotalMilliseconds);

            RasEndpoint endpoint;
            try
            {
                sw.Restart();
                endpoint = await conn.OpenEndpointAsync(AdminService, cancellationToken: ct);
                sets["open"].RecordLatency(sw.Elapsed.TotalMilliseconds);
            }
            catch (RasRemoteException ex)
            {
                sets["open"].RecordError(ex);
                return;
            }

            await using (endpoint)
            {
                sw.Restart();
                try
                {
                    await endpoint.ExecuteVoidAsync(new AuthenticateRequest(clusterId, "", ""), AdminProtoVersion, ct);
                    sets["authenticate"].RecordLatency(sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    sets["authenticate"].RecordError(ex);
                    return;
                }

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    sw.Restart();
                    var sessions = await endpoint.ExecuteAsync(new GetSessionsRequest(clusterId), AdminProtoVersion, ct);
                    sets["getSessions"].RecordLatency(sw.Elapsed.TotalMilliseconds);
                    sets["getSessions"].RecordItems(sessions.Sessions.Count);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            sets["connect"].RecordError(ex);
        }
    }

    public async Task<PhaseResult> ChurnSocketKillAsync(CancellationToken ct)
    {
        var sets = new Dictionary<string, SampleSet>
        {
            ["handshake"] = new("handshake"),
            ["query"] = new("query"),
            ["attempt"] = new("attempt")
        };

        int remaining = _options.ChurnIterations;
        var workers = Enumerable.Range(0, _options.ChurnWorkers)
            .Select(async _ =>
            {
                var sw = new Stopwatch();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int iteration = Interlocked.Decrement(ref remaining);
                    if (iteration < 0) return;

                    RasConnection? conn = null;
                    try
                    {
                        conn = new RasConnection(new DnsEndPoint(_options.Host, _options.Port));
                        sw.Restart();
                        await conn.ConnectAsync(new Dictionary<string, object?> { ["client.name"] = "loadsim-churn-" + iteration }, ct);
                        sets["handshake"].RecordLatency(sw.Elapsed.TotalMilliseconds);

                        await using var endpoint = await conn.OpenEndpointAsync(AdminService, cancellationToken: ct);
                        sw.Restart();
                        await endpoint.ExecuteAsync(new GetAgentVersionRequest(), AdminProtoVersion, ct);
                        sets["query"].RecordLatency(sw.Elapsed.TotalMilliseconds);

                        sets["attempt"].RecordLatency(0);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        sets["attempt"].RecordError(ex);
                    }
                    finally
                    {
                        conn?.Abort();
                    }

                    await Task.Delay(Random.Shared.Next(20, 120), CancellationToken.None);
                }
            })
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine($"  churn done: {sets["attempt"].Summarize().Count}/{_options.ChurnIterations} ok, post-sanity {(_options.SanitySeconds)}s ...");

        var summaries = sets.Values.Select(s => s.Summarize()).ToList();

        if (_options.SanitySeconds > 0)
        {
            var clusterId = await FindClusterIdAsync(ct);
            using var sanityCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sanityCts.CancelAfter(TimeSpan.FromSeconds(_options.SanitySeconds));
            var sets2 = NewStage1Sets();
            try
            {
                await Task.WhenAll(Enumerable.Range(1, 3).Select(i => MonitoringClientLoopAsync($"sanity{i}", clusterId, sets2, sanityCts.Token)));
            }
            catch (OperationCanceledException)
            {
            }

            summaries.AddRange(sets2.Values
                .Select(s => s.Summarize())
                .Select(s => s with { Name = "sanity:" + s.Name }));
        }

        return new PhaseResult("churn-socket-kill", summaries, new Dictionary<string, string>
        {
            ["iterationsRequested"] = _options.ChurnIterations.ToString()
        });
    }

/// <summary>
    /// Хаос-стрим: одновременно мониторинг (2 клиента, жёсткий per-request таймаут как детектор
    /// дедлоков), churn с RST-обрывами и волны «connect→запрос→штатный close».
    /// Любой unexpected-ошибка/таймаут → код возврата ≠ 0.
    /// </summary>
    public async Task<PhaseResult> ChaosMixedAsync(Guid clusterId, CancellationToken ct)
    {
        var sets = new Dictionary<string, SampleSet>
        {
            ["monitor"] = new("monitor"),
            ["churn"] = new("churn"),
            ["wave"] = new("wave")
        };

        using var phase = new CancellationTokenSource(TimeSpan.FromSeconds(_options.DurationSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, phase.Token);

        var monitor = Task.Run(async () =>
        {
            var sw = new Stopwatch();
            while (!linked.IsCancellationRequested)
            {
                RasAdminClient? client = null;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var perOp = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);

                    client = new RasAdminClient(_options.Host, _options.Port, Microsoft.Extensions.Logging.Abstractions.NullLogger<RasAdminClient>.Instance);
                    sw.Restart();
                    await client.ConnectAsync(cancellationToken: perOp.Token);
                    await client.AuthenticateClusterAsync(clusterId, "", "", perOp.Token);
                    sets["monitor"].RecordLatency(sw.Elapsed.TotalMilliseconds);

                    while (!linked.IsCancellationRequested)
                    {
                        using var qto = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var q = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, qto.Token);
                        sw.Restart();
                        _ = await client.GetSessionsAsync(clusterId, q.Token);
                        _ = await client.GetWorkingProcessesAsync(clusterId, q.Token);
                        _ = await client.GetLocksAsync(clusterId, q.Token);
                        sets["monitor"].RecordLatency(sw.Elapsed.TotalMilliseconds);
                        await Task.Delay(TimeSpan.FromSeconds(2), q.Token);
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    sets["monitor"].RecordError(ex);
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                }
                finally
                {
                    if (client is not null) await client.DisposeAsync();
                }
            }
        }, CancellationToken.None);

        var churn = Task.Run(async () =>
        {
            var sw = new Stopwatch();
            var inner = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            inner.CancelAfter(TimeSpan.FromSeconds(_options.DurationSeconds));
            try
            {
                while (!inner.IsCancellationRequested)
                {
                    sw.Restart();
                    var conn = new RasConnection(new DnsEndPoint(_options.Host, _options.Port));
                    try
                    {
                        await conn.ConnectAsync(new Dictionary<string, object?> { ["client.name"] = "loadsim-chaos-churn" }, inner.Token);
                        var endpoint = await conn.OpenEndpointAsync(AdminService, cancellationToken: inner.Token);
                        await endpoint.ExecuteAsync(new GetAgentVersionRequest(), AdminProtoVersion, inner.Token);
                        sets["churn"].RecordLatency(sw.Elapsed.TotalMilliseconds);
                    }
                    catch (OperationCanceledException) when (inner.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        sets["churn"].RecordError(ex);
                    }
                    finally
                    {
                        conn.Abort(); // RST без protocol-close
                    }
                    await Task.Delay(Random.Shared.Next(10, 80), CancellationToken.None);
                }
            }
            finally
            {
                inner.Dispose();
            }
        }, CancellationToken.None);

        var waves = Task.Run(async () =>
        {
            var wave = 0;
            while (!linked.IsCancellationRequested)
            {
                wave++;
                using var hold = new CancellationTokenSource(TimeSpan.FromSeconds(Random.Shared.Next(3, 8)));
                using var waveCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, hold.Token);
                var rampSets = new Dictionary<string, SampleSet>
                {
                    ["connect"] = new($"wave{wave}:connect"),
                    ["open"] = new($"wave{wave}:open"),
                    ["authenticate"] = new($"wave{wave}:auth"),
                    ["getSessions"] = new($"wave{wave}:sessions")
                };

                var conns = Enumerable.Range(0, 8).Select(_ => RampClientAsync(rampSets, clusterId, waveCts.Token)).ToArray();
                try
                {
                    await Task.WhenAll(conns);
                }
                catch (OperationCanceledException)
                {
                }

                foreach (var set in rampSets.Values)
                {
                    var sum = set.Summarize();
                    sets["wave"].RecordLatency(sum.Count > 0 ? sum.P50Ms : 0);
                    foreach (var (key, count) in sum.Errors)
                        sets["wave"].RecordError($"[{sum.Name}] {count}× {key}");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
        }, CancellationToken.None);

        try
        {
            await Task.WhenAll(monitor, churn, waves);
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("  chaos done, post-sanity 20s ...");
        var summaries = sets.Values.Select(s => s.Summarize()).ToList();

        using var sanityCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sanityCts.CancelAfter(TimeSpan.FromSeconds(20));
        var sets2 = NewStage1Sets();
        try
        {
            await Task.WhenAll(Enumerable.Range(1, 3).Select(i => MonitoringClientLoopAsync($"sanity{i}", clusterId, sets2, sanityCts.Token)));
        }
        catch (OperationCanceledException)
        {
        }
        summaries.AddRange(sets2.Values.Select(s => s.Summarize()).Select(x => x with { Name = "sanity:" + x.Name }));

        return new PhaseResult("chaos-mixed", summaries, new Dictionary<string, string>
        {
            ["durationSeconds"] = _options.DurationSeconds.ToString()
        });
    }

    public async Task<PhaseResult> NegotiationStormAsync(CancellationToken ct)
    {
        var sets = new Dictionary<string, SampleSet>
        {
            ["fullConnect"] = new("fullConnect"),
            ["reject(v1.0)"] = new("reject(v1.0)"),
            ["forceLow(v3.0)"] = new("forceLow(v3.0)")
        };

        using var phase = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, phase.Token);

        var storm = Enumerable.Range(0, _options.NegotiationClients)
            .Select(async _ =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await using var client = new RasAdminClient(_options.Host, _options.Port, NullLogger<RasAdminClient>.Instance);
                    await client.ConnectAsync(cancellationToken: linked.Token);
                    sets["fullConnect"].RecordLatency(sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    sets["fullConnect"].RecordError(ex);
                }
            })
            .ToArray();

        var forceLow = Enumerable.Range(0, 8).Select(async _ =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await using var conn = new RasConnection(new DnsEndPoint(_options.Host, _options.Port));
                await conn.ConnectAsync(null, linked.Token);
                await using var endpoint = await conn.OpenEndpointAsync(
                    new ServiceId(RasAdminClient.AdminServiceName, "3.0"), cancellationToken: linked.Token);
                await endpoint.ExecuteAsync(new GetAgentVersionRequest(), 3, linked.Token);
                sets["forceLow(v3.0)"].RecordLatency(sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sets["forceLow(v3.0)"].RecordError(ex);
            }
        }).ToArray();

        var rejected = Enumerable.Range(0, 8).Select(async _ =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await using var conn = new RasConnection(new DnsEndPoint(_options.Host, _options.Port));
                await conn.ConnectAsync(null, linked.Token);
                await using var endpoint = await conn.OpenEndpointAsync(
                    new ServiceId(RasAdminClient.AdminServiceName, "1.0"), cancellationToken: linked.Token);
                endpoint.CloseAsync(CancellationToken.None).Wait(linked.Token);
                sets["reject(v1.0)"].RecordError("UNEXPECTED: v1.0 opened");
            }
            catch (RasRemoteException)
            {
                sets["reject(v1.0)"].RecordLatency(sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sets["reject(v1.0)"].RecordError(ex);
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(storm.Concat(forceLow).Concat(rejected));
        }
        catch (OperationCanceledException)
        {
        }

        return new PhaseResult("negotiation-storm", sets.Values.Select(s => s.Summarize()).ToList(),
            new Dictionary<string, string> { ["clients"] = _options.NegotiationClients.ToString() });
    }

    private async Task<Guid> FindClusterIdAsync(CancellationToken ct)
    {
        await using var client = new RasAdminClient(_options.Host, _options.Port, NullLogger<RasAdminClient>.Instance);
        await client.ConnectAsync(cancellationToken: ct);
        var clusters = await client.GetClustersAsync(ct);
        return clusters.Count > 0 ? clusters[0].Uuid : throw new InvalidOperationException("Нет ни одного кластера на RAS");
    }

    public async Task<Guid> PreflightAsync(CancellationToken ct)
    {
        Console.WriteLine($"Preflight: {RasAdminClient.AdminServiceName} → discover cluster at {_options.Host}:{_options.Port}");
        var clusterId = await FindClusterIdAsync(ct);
        Console.WriteLine($"  cluster: {clusterId}");
        return clusterId;
    }

    private static PhaseResult Finalize(string phase, Dictionary<string, SampleSet> sets, Dictionary<string, string> notes)
        => new(phase, sets.Values.Select(s => s.Summarize()).ToList(), notes);
}

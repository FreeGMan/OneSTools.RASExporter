using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Exporter.Core.Pipeline;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Pipeline;

/// <summary>StorageHealthGate (T7/S2): пауза при падении, возобновление по зонду, идемпотентность.</summary>
public sealed class StorageHealthGateTests
{
    [Fact]
    public async Task Initial_State_Is_Available_And_Wait_Returns_Immediately()
    {
        await using var gate = new StorageHealthGate(_ => Task.FromResult(true), TimeSpan.FromMilliseconds(50));

        Assert.True(gate.IsAvailable);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await gate.WaitUntilReadyAsync(cts.Token);
    }

    [Fact]
    public async Task MarkUnavailable_Blocks_Waiters_And_Probe_Resume()
    {
        int probeCalls = 0;
        bool probeResult = false;

        await using var gate = new StorageHealthGate(
            _ => { Interlocked.Increment(ref probeCalls); return Task.FromResult(probeResult); },
            TimeSpan.FromMilliseconds(100));

        gate.MarkUnavailable();
        Assert.False(gate.IsAvailable);

        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var waiting = gate.WaitUntilReadyAsync(waitCts.Token);

        await Task.Delay(250);
        Assert.False(waiting.IsCompleted);
        Assert.True(Volatile.Read(ref probeCalls) >= 1);

        probeResult = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await waiting.WaitAsync(timeout.Token); // зонд восстановил
        Assert.True(gate.IsAvailable);
    }

    [Fact]
    public async Task MarkAvailable_Resumes_Without_Probe()
    {
        await using var gate = new StorageHealthGate(_ => Task.FromResult(false), TimeSpan.FromSeconds(30));

        gate.MarkUnavailable();
        Assert.False(gate.IsAvailable);

        gate.MarkAvailable();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await gate.WaitUntilReadyAsync(cts.Token);
        Assert.True(gate.IsAvailable);

        // идемпотентность
        gate.MarkAvailable();
        gate.MarkUnavailable();
        Assert.False(gate.IsAvailable);
    }

    [Fact]
    public async Task Waiter_Blocked_While_Down_Resumes_On_MarkAvailable()
    {
        await using var gate = new StorageHealthGate(_ => Task.FromResult(false), TimeSpan.FromSeconds(30));
        gate.MarkUnavailable();

        var waiting = gate.WaitUntilReadyAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        gate.MarkAvailable();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await waiting.WaitAsync(cts.Token);
    }
}

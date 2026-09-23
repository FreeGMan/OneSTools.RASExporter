using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OneSTools.Ras.Exporter.Core.Pipeline;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Pipeline;

/// <summary>
/// Семантика сохранности батчей BatchProcessor (TODO.md T5): ретраи при сбое записи,
/// незавершимость in-flight flush при остановке, финальный drain без потерь.
/// </summary>
public sealed class BatchProcessorDurabilityTests
{
    [Fact]
    public async Task Transient_Write_Failures_Are_Retried_Without_Item_Loss()
    {
        var written = new List<IReadOnlyList<int>>();
        var attempts = 0;

        await using (var processor = new BatchProcessor<int>(
            async (batch, _) =>
            {
                await Task.Yield();
                Interlocked.Increment(ref attempts);
                if (Volatile.Read(ref attempts) <= 2)
                    throw new InvalidOperationException("transient");
                written.Add(batch.ToArray());
            },
            batchSize: 100,
            flushInterval: TimeSpan.FromMilliseconds(50),
            flushAttempts: 3,
            flushGrace: TimeSpan.FromSeconds(2)))
        {
            processor.Start();
            await processor.EnqueueAsync(42);
            await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 3, TimeSpan.FromSeconds(5));
        }

        Assert.Single(written);
        Assert.Equal(new[] { 42 }, written[0]);
    }

    [Fact]
    public async Task Dispose_Drains_Pending_Items()
    {
        var written = new ConcurrentBag<int>();

        await using (var processor = new BatchProcessor<int>(
            async (batch, _) => { await Task.Yield(); foreach (var x in batch) written.Add(x); },
            batchSize: 1000,
            flushInterval: TimeSpan.FromSeconds(30),
            flushGrace: TimeSpan.FromSeconds(5)))
        {
            processor.Start();
            for (int i = 0; i < 5; i++)
                await processor.EnqueueAsync(i);
        }

        Assert.Equal(5, written.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, written.OrderBy(x => x));
    }

    [Fact]
    public async Task InFlight_Flush_Is_Not_Cancelled_By_Dispose()
    {
        var writeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int delivered = 0;

        await using var processor = new BatchProcessor<int>(
            async (batch, _) =>
            {
                writeEntered.TrySetResult();
                await releaseWrite.Task;
                Volatile.Write(ref delivered, batch.Count);
            },
            batchSize: 1000,
            flushInterval: TimeSpan.FromMilliseconds(20),
            flushGrace: TimeSpan.FromSeconds(10));

        processor.Start();
        await processor.EnqueueAsync(7);
        await processor.EnqueueAsync(8);

        await writeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var dispose = processor.DisposeAsync();
        // даём диспетчеру остановки начать dispose, пока запись «в полёте»
        await Task.Delay(200);
        releaseWrite.SetResult();
        await dispose;

        // Flush не отменён токеном остановки → данные доставлены (а не потеряны, как было до T5).
        Assert.Equal(2, Volatile.Read(ref delivered));
    }

    [Fact]
    public async Task Exhausted_Retries_Drop_Batch_But_Dispose_Completes()
    {
        var attempts = 0;

        await using (var processor = new BatchProcessor<int>(
            async (batch, _) =>
            {
                await Task.Yield();
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("permanent");
            },
            batchSize: 100,
            flushInterval: TimeSpan.FromMilliseconds(50),
            flushAttempts: 2,
            flushGrace: TimeSpan.FromSeconds(1)))
        {
            processor.Start();
            await processor.EnqueueAsync(1);
            await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 2, TimeSpan.FromSeconds(5));
        }

        Assert.True(attempts >= 2);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, cts.Token);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneSTools.Ras.Exporter.Core.Pipeline;

/// <summary>
/// Обобщенный буферизующий процессор на базе System.Threading.Channels,
/// сбрасывающий накопленные данные порциями по размеру или по интервалу времени.
/// </summary>
/// <remarks>
/// Семантика записи (TODO.md T5): запись батча никогда не отменяется током останова —
/// при сбое предельное число повторов с бэкоффом, при исчерпании попыток батч отбрасывается
/// с ошибкой в лог. При остановке сначала сбрасывается всё недописанное (с укороченным
/// числом попыток и grace-таймаутом), только затем цикл завершается.
/// </remarks>
public sealed class BatchProcessor<TItem> : IAsyncDisposable
{
    private const int ShutdownFlushAttempts = 2;
    private static readonly TimeSpan ShutdownFlushGrace = TimeSpan.FromSeconds(5);

    private readonly Channel<TItem> _channel;
    private readonly Func<IReadOnlyList<TItem>, CancellationToken, Task> _writeAction;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly int _flushAttempts;
    private readonly TimeSpan _flushGrace;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private int _disposeState;

    public BatchProcessor(
        Func<IReadOnlyList<TItem>, CancellationToken, Task> writeAction,
        int batchSize = 10000,
        TimeSpan? flushInterval = null,
        ILogger? logger = null,
        int flushAttempts = 3,
        TimeSpan? flushGrace = null)
    {
        _writeAction = writeAction;
        _batchSize = batchSize > 0 ? batchSize : 10000;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        _flushAttempts = Math.Max(1, flushAttempts);
        _flushGrace = flushGrace ?? TimeSpan.FromSeconds(30);
        _logger = logger ?? NullLogger.Instance;
        _channel = Channel.CreateUnbounded<TItem>(new UnboundedChannelOptions { SingleReader = true });
    }

    public void Start()
    {
        _processingTask = Task.Run(() => ProcessLoopAsync(_cts.Token));
    }

    public ValueTask EnqueueAsync(TItem item, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(item, cancellationToken);
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new List<TItem>(_batchSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeoutCts = new CancellationTokenSource();

                bool timedOut = false;
                bool channelCompleted = false;

                // Ожидание: набить батч до размера либо до истечения интервала.
                timeoutCts.CancelAfter(_flushInterval);
                using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    try
                    {
                        while (buffer.Count < _batchSize)
                        {
                            if (await _channel.Reader.WaitToReadAsync(waitCts.Token))
                            {
                                while (buffer.Count < _batchSize && _channel.Reader.TryRead(out var item))
                                {
                                    buffer.Add(item);
                                }
                            }
                            else
                            {
                                channelCompleted = true;
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                    }
                }

                timeoutCts.Dispose();

                if (buffer.Count > 0)
                {
                    // Запись не привязана к cancellationToken: сброс может начаться уже после
                    // запроса остановки, данные терять нельзя (см. remarks).
                    await FlushWithRetryAsync(buffer, isShutdown: false);
                }

                if (channelCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка в цикле батчинга процессора {Type}", typeof(TItem).Name);
        }
        finally
        {
            while (_channel.Reader.TryRead(out var item))
            {
                buffer.Add(item);
            }

            await FlushWithRetryAsync(buffer, isShutdown: true);
        }
    }

    private async Task FlushWithRetryAsync(List<TItem> buffer, bool isShutdown)
    {
        if (buffer.Count == 0)
            return;

        int attempts = isShutdown ? Math.Min(_flushAttempts, ShutdownFlushAttempts) : _flushAttempts;
        var grace = isShutdown ? ShutdownFlushGrace : _flushGrace;

        for (int attempt = 1; ; attempt++)
        {
            using var attemptCts = new CancellationTokenSource(grace);
            try
            {
                await _writeAction(buffer, attemptCts.Token);
                buffer.Clear();
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                _logger.LogWarning(ex, "Попытка {Attempt}/{Total} записи батча ({Count} эл.) {Type} не удалась, повтор...",
                    attempt, attempts, buffer.Count, typeof(TItem).Name);
                await Task.Delay(TimeSpan.FromSeconds(attempt), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Запись батча ({Count} эл.) {Type} не удалась после {Total} попыток — батч отброшен",
                    buffer.Count, typeof(TItem).Name, attempts);
                buffer.Clear();
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            while (Volatile.Read(ref _disposeState) != 2)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
            return;
        }

        try
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
            if (_processingTask is not null)
            {
                try
                {
                    await _processingTask;
                }
                catch
                {
                    // Ignore during dispose
                }
            }
        }
        finally
        {
            _cts.Dispose();
            Volatile.Write(ref _disposeState, 2);
        }
    }
}

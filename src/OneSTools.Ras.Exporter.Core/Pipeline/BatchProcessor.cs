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
/// Семантика записи (docs/SECURITY.md §3 T3): запись батча никогда не отменяется током останова —
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
    private readonly Action<bool>? _availabilityCallback;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private int _disposeState;
    private long _droppedItems;

    public long DroppedItems => Interlocked.Read(ref _droppedItems);

    public BatchProcessor(
        Func<IReadOnlyList<TItem>, CancellationToken, Task> writeAction,
        int batchSize = 10000,
        TimeSpan? flushInterval = null,
        ILogger? logger = null,
        int flushAttempts = 3,
        TimeSpan? flushGrace = null,
        Action<bool>? availabilityCallback = null)
    {
        _availabilityCallback = availabilityCallback;
        _writeAction = writeAction;
        _batchSize = batchSize > 0 ? batchSize : 10000;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        _flushAttempts = Math.Max(1, flushAttempts);
        _flushGrace = flushGrace ?? TimeSpan.FromSeconds(30);
        _logger = logger ?? NullLogger.Instance;
        // Страховка: при живом StorageHealthGate до предела не доходит;
        // FullMode=Wait даёт backpressure вместо неограниченной аллокации.
        _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
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
                    // Сброс может начаться уже после запроса остановки — данные терять нельзя.
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
                _availabilityCallback?.Invoke(true);
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                RetainFailedItems(buffer, ex);
                // первый сбой закрывает гейт: коллекторы встают на паузу сразу (меньше потерь)
                _availabilityCallback?.Invoke(false);
                _logger.LogWarning(ex, "Попытка {Attempt}/{Total} записи батча ({Count} эл.) {Type} не удалась, повтор...",
                    attempt, attempts, buffer.Count, typeof(TItem).Name);
                await Task.Delay(TimeSpan.FromSeconds(attempt), CancellationToken.None);
            }
            catch (Exception ex)
            {
                RetainFailedItems(buffer, ex);
                _logger.LogError(ex, "Запись батча ({Count} эл.) {Type} не удалась после {Total} попыток — батч отброшен",
                    buffer.Count, typeof(TItem).Name, attempts);
                _availabilityCallback?.Invoke(false);
                Interlocked.Add(ref _droppedItems, buffer.Count);
                buffer.Clear();
                return;
            }
        }
    }

    private static void RetainFailedItems(List<TItem> buffer, Exception error)
    {
        if (error is not PartialBatchWriteException<TItem> partial) return;

        buffer.Clear();
        buffer.AddRange(partial.FailedItems);
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

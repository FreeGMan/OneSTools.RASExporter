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
public sealed class BatchProcessor<TItem> : IAsyncDisposable
{
    private readonly Channel<TItem> _channel;
    private readonly Func<IReadOnlyList<TItem>, CancellationToken, Task> _writeAction;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    public BatchProcessor(
        Func<IReadOnlyList<TItem>, CancellationToken, Task> writeAction,
        int batchSize = 10000,
        TimeSpan? flushInterval = null,
        ILogger? logger = null)
    {
        _writeAction = writeAction;
        _batchSize = batchSize > 0 ? batchSize : 10000;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
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
        var timeoutCts = new CancellationTokenSource();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for either item or timeout
                timeoutCts.CancelAfter(_flushInterval);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    while (buffer.Count < _batchSize)
                    {
                        if (await _channel.Reader.WaitToReadAsync(linkedCts.Token))
                        {
                            while (buffer.Count < _batchSize && _channel.Reader.TryRead(out var item))
                            {
                                buffer.Add(item);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Flush interval timer ticked
                }

                if (buffer.Count > 0)
                {
                    await FlushBufferAsync(buffer, cancellationToken);
                }

                timeoutCts.Dispose();
                timeoutCts = new CancellationTokenSource();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка в цикле батчинга процессора {Type}", typeof(TItem).Name);
        }
        finally
        {
            timeoutCts.Dispose();
            // Drain remaining items on shutdown
            while (_channel.Reader.TryRead(out var item))
            {
                buffer.Add(item);
            }
            if (buffer.Count > 0)
            {
                try
                {
                    await FlushBufferAsync(buffer, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при финальном сбросе батча {Type}", typeof(TItem).Name);
                }
            }
        }
    }

    private async Task FlushBufferAsync(List<TItem> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await _writeAction(buffer, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при записи батча ({Count} элементов) {Type}", buffer.Count, typeof(TItem).Name);
        }
        finally
        {
            buffer.Clear();
        }
    }

    private int _disposeState; // 0 = жив, 1 = dispose запрошен, 2 = завершён

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            // Повторный dispose: ждём завершения первого.
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

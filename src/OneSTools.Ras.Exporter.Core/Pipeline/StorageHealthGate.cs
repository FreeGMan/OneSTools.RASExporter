using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneSTools.Ras.Exporter.Core.Pipeline;

/// <summary>
/// Гейт доступности хранилища (ClickHouse): при недоступности приостанавливает опросы RAS
/// (коллекторы ждут <see cref="WaitUntilReadyAsync"/> перед каждым циклом) и периодически
/// пробует восстановление через зонд хранилища. Решение о паузе вместо бесконечного накопления
/// очередей — см. docs/SECURITY.md (T7/S2).
/// </summary>
public sealed class StorageHealthGate : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<bool>> _probe;
    private readonly TimeSpan _probeInterval;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();

    private volatile TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _probing; // 0 = нет активного цикла проб

    public StorageHealthGate(
        Func<CancellationToken, Task<bool>> probe,
        TimeSpan? probeInterval = null,
        ILogger? logger = null)
    {
        _probe = probe;
        _probeInterval = probeInterval ?? TimeSpan.FromSeconds(30);
        _logger = logger ?? NullLogger.Instance;
        _readyTcs.TrySetResult(true);
    }

    public bool IsAvailable => _readyTcs.Task.IsCompleted && _readyTcs.Task.Result;

    /// <summary>Ждёт доступности хранилища; при MarkUnavailable ожидающие получают
    /// false-сигнал и переходят в ожидание на новом маркере до восстановления.</summary>
    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var tcs = _readyTcs;
            if (tcs.Task.IsCompleted && tcs.Task.Result)
                return;
            await tcs.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Хранилище упало: закрываем готовности и стартуем цикл проб (идемпотентно).</summary>
    public void MarkUnavailable()
    {
        var tcs = _readyTcs;
        if (!tcs.Task.IsCompleted)
            return; // уже unavailable

        var next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _readyTcs, next, tcs) != tcs)
            return; // гонка: другой поток уже перевёл

        tcs.TrySetResult(false); // разблокировать ожидающих — перепроверят в цикле
        _logger.LogWarning("Хранилище недоступно — опросы RAS приостановлены, повторная проверка каждые {Interval}", _probeInterval);
        StartProbing();
    }

    /// <summary>Успешная запись/проба: открываем готовности (идемпотентно).</summary>
    public void MarkAvailable()
    {
        if (!IsAvailable)
        {
            _logger.LogInformation("Хранилище снова доступно — опросы возобновлены");
            _readyTcs.TrySetResult(true);
        }
    }

    private void StartProbing()
    {
        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested && !IsAvailable)
                {
                    await Task.Delay(_probeInterval, _stop.Token).ConfigureAwait(false);

                    bool ok;
                    try
                    {
                        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        probeCts.CancelAfter(TimeSpan.FromSeconds(10));
                        ok = await _probe(probeCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        ok = false;
                        _logger.LogDebug(ex, "Зонд хранилища неудачен");
                    }

                    if (ok)
                    {
                        _readyTcs.TrySetResult(true);
                        _logger.LogInformation("Хранилище восстановлено (зонд) — опросы возобновлены");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Volatile.Write(ref _probing, 0);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { _readyTcs.TrySetResult(true); } catch { }
        _stop.Dispose();
        await Task.CompletedTask;
    }
}

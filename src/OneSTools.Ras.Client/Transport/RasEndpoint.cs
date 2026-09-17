using System;
using System.Threading;
using System.Threading.Tasks;

namespace OneSTools.Ras.Client.Transport;

/// <summary>
/// Представляет виртуальный канал (эндпоинт) поверх соединения SWP (RasConnection).
/// </summary>
public sealed class RasEndpoint : IAsyncDisposable
{
    public int EndpointId { get; }
    public ServiceId ServiceId { get; }
    public RasConnection Connection { get; }
    public bool UseCompression { get; }

    public RasEndpoint(int endpointId, ServiceId serviceId, RasConnection connection, bool useCompression)
    {
        EndpointId = endpointId;
        ServiceId = serviceId;
        Connection = connection;
        UseCompression = useCompression;
    }

    /// <summary>
    /// Отправляет запрос к эндпоинту и ожидает ответ.
    /// </summary>
    public Task<byte[]> SendMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        return Connection.SendEndpointMessageAsync(EndpointId, payload, UseCompression, cancellationToken);
    }

    /// <summary>
    /// Закрывает данный эндпоинт.
    /// </summary>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        return Connection.CloseEndpointAsync(EndpointId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync();
        }
        catch
        {
            // Ignore on dispose
        }
    }

    public override string ToString() => $"RasEndpoint(Id={EndpointId}, Service={ServiceId}, Compression={UseCompression})";
}

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Wire;

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
    /// Выполняет типизированный запрос к API и десериализует ответ.
    /// </summary>
    public async Task<TResponse> ExecuteAsync<TResponse>(IRasRequest<TResponse> request, int protocolVersion = 0, CancellationToken cancellationToken = default)
        where TResponse : IRasResponse<TResponse>
    {
        byte[] requestBytes = FormatRequest(request, protocolVersion);
        byte[] responseBytes = await SendMessageAsync(requestBytes, cancellationToken);
        return ParseResponse<TResponse>(responseBytes, protocolVersion);
    }

    /// <summary>
    /// Выполняет запрос к API, не возвращающий данных (void).
    /// </summary>
    public async Task ExecuteVoidAsync(IRasVoidRequest request, int protocolVersion = 0, CancellationToken cancellationToken = default)
    {
        byte[] requestBytes = FormatVoidRequest(request, protocolVersion);
        await SendMessageAsync(requestBytes, cancellationToken);
    }

    private static byte[] FormatRequest<TResponse>(IRasRequest<TResponse> request, int protocolVersion)
        where TResponse : IRasResponse<TResponse>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        writer.WriteByte((byte)request.MessageType);
        request.Serialize(ref writer, protocolVersion);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] FormatVoidRequest(IRasVoidRequest request, int protocolVersion)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        writer.WriteByte((byte)request.MessageType);
        request.Serialize(ref writer, protocolVersion);
        return buffer.WrittenSpan.ToArray();
    }

    private static TResponse ParseResponse<TResponse>(byte[] responseBytes, int protocolVersion)
        where TResponse : IRasResponse<TResponse>
    {
        var seq = new ReadOnlySequence<byte>(responseBytes);
        var reader = new RasReader(seq);

        if (reader.Remaining > 0)
        {
            _ = reader.ReadByte(); // message type
        }

        return TResponse.Deserialize(ref reader, protocolVersion);
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

using System.Buffers;
using System.Linq;
using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Transport;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.MockServer.Handlers;

/// <summary>
/// Обработка одного TCP-подключения клиента RAS:
/// Negotiate (raw 8 байт) → Connect (type 1) → ConnectAck (type 2) → цикл фреймов.
/// Источник: docs/TRANSPORT_SPEC.md, разделы 4–8; поведение сервиса администрирования
/// и отказов — по реальному RAS 8.3.25.1520 (см. tests/.../RealRas, TODO.md п.2/7).
/// </summary>
public sealed class ClientSession
{
    public const string KeepAliveTimeoutKey = "nipple.swp.connection.keep_alive.timeout";

    public const string AdminServiceName = "v8.service.Admin.Cluster";
    public const string DefaultMaxAdminVersion = "14.0";

    public static IReadOnlyList<string> SupportedAdminVersions(int majorMax)
    {
        if (majorMax > 14) majorMax = 14;
        return Enumerable.Range(3, majorMax - 2).Select(v => v + ".0").ToList();
    }

    private const string RightsDeniedClass = "v8.service.Admin.Cluster#Rights";
    private const string RightsDeniedMessage = "Insufficient user rights to manage the cluster Mock-Cluster-01";

    private readonly TcpClient _client;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<string> _supportedAdminVersions;
    private int _nextEndpointId = 1;
    private readonly Dictionary<int, (bool Compression, int Version)> _endpoints = new();
    private bool _clusterAuthenticated;

    public ClientSession(TcpClient client, ILogger logger, int maxAdminVersionMajor = 14)
    {
        _client = client;
        _logger = logger;
        _supportedAdminVersions = SupportedAdminVersions(maxAdminVersionMajor);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using (_client)
            using (var stream = _client.GetStream())
            {
                var reader = new Framing.SwpFrameReader(stream);

                await HandleNegotiateAsync(reader, cancellationToken);
                await HandleConnectAsync(reader, stream, cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    Framing.SwpFrame frame = await reader.ReadFrameAsync(cancellationToken);
                    _logger.LogInformation(
                        "Получен фрейм: type={Type} ({Name}), size={Size}",
                        frame.Type, (SwpMessageType)frame.Type, frame.Payload.Length);

                    if (frame.Type == (byte)SwpMessageType.KeepAlive)
                    {
                        await WriteFrameAsync(stream, (byte)SwpMessageType.KeepAlive, Array.Empty<byte>(), cancellationToken);
                    }
                    else if (frame.Type == (byte)SwpMessageType.EndpointOpen)
                    {
                        await HandleEndpointOpenAsync(frame.Payload, stream, cancellationToken);
                    }
                    else if (frame.Type == (byte)SwpMessageType.EndpointMessage)
                    {
                        await HandleEndpointMessageAsync(frame.Payload, stream, cancellationToken);
                    }
                    else if (frame.Type == (byte)SwpMessageType.EndpointClose)
                    {
                        HandleEndpointClose(frame.Payload);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or SocketException)
        {
            _logger.LogInformation("Клиент отключился: {Message}", ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Сессия остановлена");
        }
    }

    private async Task HandleNegotiateAsync(Framing.SwpFrameReader reader, CancellationToken cancellationToken)
    {
        byte[] raw = await reader.ReadBytesAsync(8, cancellationToken);
        int magic = BinaryPrimitives.ReadInt32BigEndian(raw);
        short protocolVersion = BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(4, 2));
        short codecVersion = BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(6, 2));

        _logger.LogInformation(
            "Negotiate: magic={Magic} (ожидается {Expected}), protocolVersion={ProtocolVersion}, codecVersion={CodecVersion}",
            magic, SwpProtocolConstants.Magic, protocolVersion, codecVersion);
    }

    private async Task HandleConnectAsync(Framing.SwpFrameReader reader, Stream stream, CancellationToken cancellationToken)
    {
        Framing.SwpFrame connect = await reader.ReadFrameAsync(cancellationToken);
        if (connect.Type != (byte)SwpMessageType.Connect)
        {
            throw new InvalidDataException($"Ожидался Connect (1), получен type={connect.Type}");
        }

        var ackWriter = new Framing.SwpFrameWriter();
        ackWriter.WriteNullableSize(1);
        ackWriter.WriteString(KeepAliveTimeoutKey);
        ackWriter.WriteTypedString("20");

        await WriteFrameAsync(stream, (byte)SwpMessageType.ConnectAck, ackWriter.GetWrittenBytes().ToArray(), cancellationToken);
        _logger.LogInformation("Подключение принято, отправлен ConnectAck");
    }

    private async Task HandleEndpointOpenAsync(byte[] payload, Stream stream, CancellationToken cancellationToken)
    {
        var (name, version, parameters) = ParseEndpointOpen(payload);

        if (name != AdminServiceName || version is null || !_supportedAdminVersions.Contains(version))
        {
            await RejectUnsupportedServiceAsync(stream, name, version, cancellationToken);
            return;
        }

        int endpointId = _nextEndpointId++;
        bool useCompression = parameters.TryGetValue("endpoint.encoding", out var enc) && enc is string s && s.Equals("deflate", StringComparison.OrdinalIgnoreCase);
        int protocolVersion = int.TryParse(version!.Split('.')[0], out var major) ? major : 0;
        _endpoints[endpointId] = (useCompression, protocolVersion);

        _logger.LogInformation("Открытие эндпоинта: Service={Name}:{Version}, ID={Id}, Compression={Comp}", name, version, endpointId, useCompression);

        byte[] ackBytes = FormatEndpointOpenAck(name, version, endpointId, useCompression);
        await WriteFrameAsync(stream, (byte)SwpMessageType.EndpointOpenAck, ackBytes, cancellationToken);
    }

    /// <summary>
    /// Формат отказа неоткрытого сервиса — как у реального RAS:
    /// swp.Endpoint («Mount point error») с cause v8.service.UnsupportedService
    /// и списком supported=... (по нему клиентский negotiation ограничивает перебор).
    /// </summary>
    private async Task RejectUnsupportedServiceAsync(Stream stream, string? name, string? version, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Отказ в открытии эндпоинта: Service={Name}:{Version}", name, version);

        string supportedList = string.Join(",", _supportedAdminVersions);

        var inner = new RasRemoteException(
            "v8.service.UnsupportedService",
            $"Service is not supported [name='{name}', version='{version}', supported={supportedList}]",
            Array.Empty<RasRemoteStackFrame>());
        var outer = new RasRemoteException("swp.Endpoint", "Mount point error", Array.Empty<RasRemoteStackFrame>(), inner);

        var failureBuffer = new ArrayBufferWriter<byte>();
        var failureWriter = new RasWriter(failureBuffer);
        failureWriter.WriteString(name);
        failureWriter.WriteString(version);
        failureWriter.WriteNullableSize(null);
        RasThrowableSerializer.WriteThrowable(ref failureWriter, outer);

        await WriteFrameAsync(stream, (byte)SwpMessageType.EndpointFailure, failureBuffer.WrittenMemory.ToArray(), cancellationToken);
    }

    private static (string? Name, string? Version, Dictionary<string, object?> Parameters) ParseEndpointOpen(byte[] payload)
    {
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);
        string? name = reader.ReadString();
        string? version = reader.ReadString();
        var parameters = RasParametersSerializer.ReadParameters(ref reader);
        return (name, version, parameters);
    }

    private static byte[] FormatEndpointOpenAck(string? name, string? version, int endpointId, bool useCompression)
    {
        var ackParams = new Dictionary<string, object?>();
        if (useCompression)
        {
            ackParams["endpoint.encoding"] = "deflate";
        }

        var ackBuffer = new ArrayBufferWriter<byte>();
        var ackWriter = new RasWriter(ackBuffer);
        ackWriter.WriteString(name);
        ackWriter.WriteString(version);
        ackWriter.WriteEndpointId(endpointId);
        RasParametersSerializer.WriteParameters(ref ackWriter, ackParams);
        return ackBuffer.WrittenSpan.ToArray();
    }

    private async Task HandleEndpointMessageAsync(byte[] payload, Stream stream, CancellationToken cancellationToken)
    {
        var (endpointId, format, body) = ParseEndpointMessage(payload);
        if (endpointId is null) return;

        if ((format & 1) != 0)
        {
            body = RasCompression.Inflate(body);
        }

        _logger.LogInformation("EndpointMessage для ID={Id}, format={Format}, bodyLen={Len}", endpointId, format, body.Length);

        int responseVersion = _endpoints.TryGetValue(endpointId.Value, out var ep) ? ep.Version : 0;
        byte[] responseBody = GenerateApiResponse(body, responseVersion);

        bool useCompression = _endpoints.TryGetValue(endpointId.Value, out var endpoint) && endpoint.Compression;
        short responseFormat = 0;
        if (useCompression && responseBody.Length > 0 && responseBody[0] != 0x00)
        {
            responseBody = RasCompression.Deflate(responseBody);
            responseFormat = 1;
        }

        byte[] responseFrame = FormatEndpointMessageResponse(endpointId.Value, responseFormat, responseBody);
        await WriteFrameAsync(stream, (byte)SwpMessageType.EndpointMessage, responseFrame, cancellationToken);
    }

    private byte[] GenerateApiResponse(byte[] body, int protocolVersion)
    {
        if (body.Length == 0) return new byte[] { 0x00 };

        byte kind = body[0];
        if (kind != 0x01 || body.Length < 2)
        {
            return new byte[] { 0x00 }; // VOID
        }

        var reqType = (MessageType)body[1];

        if (NeedsClusterAuth(reqType) && !_clusterAuthenticated)
        {
            return CreateRightsExceptionBody();
        }

        var respBuffer = new ArrayBufferWriter<byte>();
        var respWriter = new RasWriter(respBuffer);
        respWriter.WriteByte(0x01); // EndpointDataType.MESSAGE

        switch (reqType)
        {
            case MessageType.GetClustersRequest:
                respWriter.WriteByte((byte)MessageType.GetClustersResponse);
                new GetClustersResponse
                {
                    Clusters = new[]
                    {
                        new ClusterInfo
                        {
                            Uuid = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Name = "Mock-Cluster-01",
                            Host = "mock-server",
                            Port = 1541
                        }
                    }
                }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.GetSessionsRequest:
            case MessageType.GetInfoBaseSessionsRequest:
                respWriter.WriteByte((byte)MessageType.GetSessionsResponse);
                new GetSessionsResponse
                {
                    Sessions = new[]
                    {
                        new SessionInfo
                        {
                            Uuid = Guid.NewGuid(),
                            AppId = "1CV8C",
                            UserName = "TestUser",
                            Id = 1,
                            StartedAt = DateTime.UtcNow.AddMinutes(-30),
                            LastActiveAt = DateTime.UtcNow
                        }
                    }
                }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.GetWorkingProcessesRequest:
                respWriter.WriteByte((byte)MessageType.GetWorkingProcessesResponse);
                new GetWorkingProcessesResponse
                {
                    Processes = new[]
                    {
                        new ProcessInfo
                        {
                            Uuid = Guid.NewGuid(),
                            Host = "mock-server",
                            Port = 1560,
                            Pid = "1001",
                            Running = true,
                            Enable = true
                        }
                    }
                }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.GetConnectionsShortRequest:
            case MessageType.GetInfoBaseConnectionsShortRequest:
                respWriter.WriteByte((byte)MessageType.GetConnectionsShortResponse);
                new GetConnectionsShortResponse
                {
                    Connections = new[]
                    {
                        new ConnectionInfo
                        {
                            Uuid = Guid.NewGuid(),
                            Application = "1CV8C",
                            Host = "127.0.0.1",
                            Id = 101,
                            SessionId = 1
                        }
                    }
                }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.GetLocksRequest:
            case MessageType.GetInfoBaseLocksRequest:
            case MessageType.GetConnectionLocksRequest:
            case MessageType.GetSessionLocksRequest:
                respWriter.WriteByte((byte)MessageType.GetLocksResponse);
                new GetLocksResponse
                {
                    Locks = new[]
                    {
                        new LockInfo
                        {
                            ConnectionId = Guid.NewGuid(),
                            Description = "Mock Lock",
                            LockedAt = DateTime.UtcNow,
                            ObjectId = Guid.NewGuid(),
                            SessionId = Guid.NewGuid()
                        }
                    }
                }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.GetInfoBasesShortRequest:
                respWriter.WriteByte((byte)MessageType.GetInfoBasesShortResponse);
                new GetInfoBasesShortResponse
                {
                    InfoBases = new[]
                    {
                        new InfobaseSummaryInfo
                        {
                            Uuid = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            Name = "mock_db",
                            Descr = "Mock Infobase"
                        }
                    }
                }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.GetAgentVersionRequest:
                respWriter.WriteByte((byte)MessageType.GetAgentVersionResponse);
                new GetAgentVersionResponse { Version = "8.3.24.1342" }.Serialize(ref respWriter, protocolVersion);
                return respBuffer.WrittenSpan.ToArray();

            case MessageType.AuthenticateRequest:
            case MessageType.AddAuthenticationRequest:
                _clusterAuthenticated = true;
                return new byte[] { 0x00 }; // VOID

            case MessageType.AuthenticateAgentRequest:
            case MessageType.TerminateSessionRequest:
            case MessageType.DisconnectRequest:
                return new byte[] { 0x00 }; // VOID

            default:
                // Echo fallback
                return body;
        }
    }

    private static bool NeedsClusterAuth(MessageType type) => type switch
    {
        MessageType.GetSessionsRequest or
        MessageType.GetInfoBaseSessionsRequest or
        MessageType.GetSessionInfoRequest or
        MessageType.TerminateSessionRequest or
        MessageType.GetWorkingProcessesRequest or
        MessageType.GetWorkingProcessInfoRequest or
        MessageType.GetServerWorkingProcessesRequest or
        MessageType.GetConnectionsShortRequest or
        MessageType.GetInfoBaseConnectionsShortRequest or
        MessageType.GetConnectionInfoShortRequest or
        MessageType.GetInfoBaseConnectionsRequest or
        MessageType.DisconnectRequest or
        MessageType.GetLocksRequest or
        MessageType.GetInfoBaseLocksRequest or
        MessageType.GetConnectionLocksRequest or
        MessageType.GetSessionLocksRequest or
        MessageType.GetWorkingServersRequest or
        MessageType.GetWorkingServerInfoRequest or
        MessageType.GetClusterManagersRequest or
        MessageType.GetClusterManagerInfoRequest or
        MessageType.GetClusterServicesRequest => true,
        _ => false
    };

    private static byte[] CreateRightsExceptionBody()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        writer.WriteByte(0xFF); // EndpointDataType.EXCEPTION
        var rights = new RasRemoteException(RightsDeniedClass, RightsDeniedMessage, Array.Empty<RasRemoteStackFrame>());
        RasThrowableSerializer.WriteThrowable(ref writer, rights);
        return buffer.WrittenSpan.ToArray();
    }

    private static (int? EndpointId, short Format, byte[] Body) ParseEndpointMessage(byte[] payload)
    {
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);
        int? endpointId = reader.ReadEndpointId();
        short format = reader.ReadShort();
        byte[] body = reader.ReadRemainingBytes();
        return (endpointId, format, body);
    }

    private static byte[] FormatEndpointMessageResponse(int endpointId, short format, byte[] body)
    {
        var responseBuffer = new ArrayBufferWriter<byte>();
        var responseWriter = new RasWriter(responseBuffer);
        responseWriter.WriteEndpointId(endpointId);
        responseWriter.WriteShort(format);
        responseWriter.WriteBytes(body);
        return responseBuffer.WrittenSpan.ToArray();
    }

    private void HandleEndpointClose(byte[] payload)
    {
        var seq = new ReadOnlySequence<byte>(payload);
        var reader = new RasReader(seq);
        int? endpointId = reader.ReadEndpointId();
        if (endpointId is not null)
        {
            _endpoints.Remove(endpointId.Value);
            _logger.LogInformation("Эндпоинт {Id} закрыт клиентом", endpointId);
        }
    }

    private static async Task WriteFrameAsync(Stream stream, byte type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var writer = new Framing.SwpFrameWriter();
        writer.WriteByte(type);
        writer.WriteSize(payload.Length);

        byte[] head = writer.GetWrittenBytes().ToArray();
        await stream.WriteAsync(head, cancellationToken);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }
}
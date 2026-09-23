using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

/// <summary>Запрос SWP-сообщение «GetConnectionsShortRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetConnectionsShortRequest(Guid ClusterId) : IRasRequest<GetConnectionsShortResponse>
{
    public MessageType MessageType => MessageType.GetConnectionsShortRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetConnectionsShortRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetConnectionsShortRequest(reader.ReadUuid());
    }
}

/// <summary>Ответ SWP-сообщение «GetConnectionsShortResponse». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetConnectionsShortResponse : IRasResponse<GetConnectionsShortResponse>
{
    public IReadOnlyList<ConnectionInfo> Connections { get; init; } = Array.Empty<ConnectionInfo>();

    public MessageType MessageType => MessageType.GetConnectionsShortResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Connections.Count);
        foreach (var conn in Connections)
        {
            conn.Serialize(ref writer, version);
        }
    }

    public static GetConnectionsShortResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<ConnectionInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ConnectionInfo.Deserialize(ref reader, version));
        }
        return new GetConnectionsShortResponse { Connections = list };
    }
}

/// <summary>Запрос SWP-сообщение «GetInfoBaseConnectionsShortRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetInfoBaseConnectionsShortRequest(Guid ClusterId, Guid InfobaseId) : IRasRequest<GetInfoBaseConnectionsShortResponse>
{
    public MessageType MessageType => MessageType.GetInfoBaseConnectionsShortRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(InfobaseId);
    }

    public static GetInfoBaseConnectionsShortRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBaseConnectionsShortRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

/// <summary>Ответ SWP-сообщение «GetInfoBaseConnectionsShortResponse». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetInfoBaseConnectionsShortResponse : IRasResponse<GetInfoBaseConnectionsShortResponse>
{
    public IReadOnlyList<ConnectionInfo> Connections { get; init; } = Array.Empty<ConnectionInfo>();

    public MessageType MessageType => MessageType.GetInfoBaseConnectionsShortResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Connections.Count);
        foreach (var conn in Connections)
        {
            conn.Serialize(ref writer, version);
        }
    }

    public static GetInfoBaseConnectionsShortResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<ConnectionInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ConnectionInfo.Deserialize(ref reader, version));
        }
        return new GetInfoBaseConnectionsShortResponse { Connections = list };
    }
}

/// <summary>Запрос SWP-сообщение «DisconnectRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record DisconnectRequest(Guid ClusterId, Guid ProcessId, Guid ConnectionId) : IRasVoidRequest
{
    public MessageType MessageType => MessageType.DisconnectRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(ProcessId);
        writer.WriteUuid(ConnectionId);
    }
}

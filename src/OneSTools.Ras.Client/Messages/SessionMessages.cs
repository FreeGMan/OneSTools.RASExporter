using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public sealed record GetSessionsRequest(Guid ClusterId) : IRasRequest<GetSessionsResponse>
{
    public MessageType MessageType => MessageType.GetSessionsRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetSessionsRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetSessionsRequest(reader.ReadUuid());
    }
}

public sealed record GetSessionsResponse : IRasResponse<GetSessionsResponse>
{
    public IReadOnlyList<SessionInfo> Sessions { get; init; } = Array.Empty<SessionInfo>();

    public MessageType MessageType => MessageType.GetSessionsResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Sessions.Count);
        foreach (var session in Sessions)
        {
            session.Serialize(ref writer, version);
        }
    }

    public static GetSessionsResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<SessionInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(SessionInfo.Deserialize(ref reader, version));
        }
        return new GetSessionsResponse { Sessions = list };
    }
}

public sealed record GetInfoBaseSessionsRequest(Guid ClusterId, Guid InfobaseId) : IRasRequest<GetInfoBaseSessionsResponse>
{
    public MessageType MessageType => MessageType.GetInfoBaseSessionsRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(InfobaseId);
    }

    public static GetInfoBaseSessionsRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBaseSessionsRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

public sealed record GetInfoBaseSessionsResponse : IRasResponse<GetInfoBaseSessionsResponse>
{
    public IReadOnlyList<SessionInfo> Sessions { get; init; } = Array.Empty<SessionInfo>();

    public MessageType MessageType => MessageType.GetInfoBaseSessionsResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Sessions.Count);
        foreach (var session in Sessions)
        {
            session.Serialize(ref writer, version);
        }
    }

    public static GetInfoBaseSessionsResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<SessionInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(SessionInfo.Deserialize(ref reader, version));
        }
        return new GetInfoBaseSessionsResponse { Sessions = list };
    }
}

public sealed record GetSessionInfoRequest(Guid ClusterId, Guid SessionId) : IRasRequest<GetSessionInfoResponse>
{
    public MessageType MessageType => MessageType.GetSessionInfoRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(SessionId);
    }

    public static GetSessionInfoRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetSessionInfoRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

public sealed record GetSessionInfoResponse : IRasResponse<GetSessionInfoResponse>
{
    public SessionInfo Info { get; init; } = new();

    public MessageType MessageType => MessageType.GetSessionInfoResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        Info.Serialize(ref writer, version);
    }

    public static GetSessionInfoResponse Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetSessionInfoResponse { Info = SessionInfo.Deserialize(ref reader, version) };
    }
}

public sealed record TerminateSessionRequest(Guid ClusterId, Guid SessionId, string? Message = null) : IRasVoidRequest
{
    public MessageType MessageType => MessageType.TerminateSessionRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(SessionId);
        if (Message is not null)
        {
            writer.WriteString(Message);
        }
    }
}

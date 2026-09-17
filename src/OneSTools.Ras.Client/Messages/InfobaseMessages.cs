using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public sealed record GetInfoBasesShortRequest(Guid ClusterId) : IRasRequest<GetInfoBasesShortResponse>
{
    public MessageType MessageType => MessageType.GetInfoBasesShortRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetInfoBasesShortRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBasesShortRequest(reader.ReadUuid());
    }
}

public sealed record GetInfoBasesShortResponse : IRasResponse<GetInfoBasesShortResponse>
{
    public IReadOnlyList<InfobaseSummaryInfo> InfoBases { get; init; } = Array.Empty<InfobaseSummaryInfo>();

    public MessageType MessageType => MessageType.GetInfoBasesShortResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(InfoBases.Count);
        foreach (var ib in InfoBases)
        {
            ib.Serialize(ref writer, version);
        }
    }

    public static GetInfoBasesShortResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadSize();
        var list = new List<InfobaseSummaryInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(InfobaseSummaryInfo.Deserialize(ref reader, version));
        }
        return new GetInfoBasesShortResponse { InfoBases = list };
    }
}

public sealed record GetInfoBasesRequest(Guid ClusterId) : IRasRequest<GetInfoBasesResponse>
{
    public MessageType MessageType => MessageType.GetInfoBasesRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetInfoBasesRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBasesRequest(reader.ReadUuid());
    }
}

public sealed record GetInfoBasesResponse : IRasResponse<GetInfoBasesResponse>
{
    public IReadOnlyList<InfobaseInfo> InfoBases { get; init; } = Array.Empty<InfobaseInfo>();

    public MessageType MessageType => MessageType.GetInfoBasesResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(InfoBases.Count);
        foreach (var ib in InfoBases)
        {
            ib.Serialize(ref writer, version);
        }
    }

    public static GetInfoBasesResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadSize();
        var list = new List<InfobaseInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(InfobaseInfo.Deserialize(ref reader, version));
        }
        return new GetInfoBasesResponse { InfoBases = list };
    }
}

public sealed record GetInfoBaseInfoRequest(Guid ClusterId, Guid InfobaseId) : IRasRequest<GetInfoBaseInfoResponse>
{
    public MessageType MessageType => MessageType.GetInfoBaseInfoRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(InfobaseId);
    }

    public static GetInfoBaseInfoRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBaseInfoRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

public sealed record GetInfoBaseInfoResponse : IRasResponse<GetInfoBaseInfoResponse>
{
    public InfobaseInfo Info { get; init; } = new();

    public MessageType MessageType => MessageType.GetInfoBaseInfoResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        Info.Serialize(ref writer, version);
    }

    public static GetInfoBaseInfoResponse Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBaseInfoResponse { Info = InfobaseInfo.Deserialize(ref reader, version) };
    }
}

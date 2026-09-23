using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public sealed record GetClustersRequest : IRasRequest<GetClustersResponse>
{
    public MessageType MessageType => MessageType.GetClustersRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        // No fields
    }

    public static GetClustersRequest Deserialize(ref RasReader reader, int version = 0) => new();
}

public sealed record GetClustersResponse : IRasResponse<GetClustersResponse>
{
    public IReadOnlyList<ClusterInfo> Clusters { get; init; } = Array.Empty<ClusterInfo>();

    public MessageType MessageType => MessageType.GetClustersResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Clusters.Count);
        foreach (var cluster in Clusters)
        {
            cluster.Serialize(ref writer, version);
        }
    }

    public static GetClustersResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<ClusterInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ClusterInfo.Deserialize(ref reader, version));
        }
        return new GetClustersResponse { Clusters = list };
    }
}

public sealed record GetClusterInfoRequest(Guid ClusterId) : IRasRequest<GetClusterInfoResponse>
{
    public MessageType MessageType => MessageType.GetClusterInfoRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetClusterInfoRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetClusterInfoRequest(reader.ReadUuid());
    }
}

public sealed record GetClusterInfoResponse : IRasResponse<GetClusterInfoResponse>
{
    public ClusterInfo Info { get; init; } = new();

    public MessageType MessageType => MessageType.GetClusterInfoResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        Info.Serialize(ref writer, version);
    }

    public static GetClusterInfoResponse Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetClusterInfoResponse { Info = ClusterInfo.Deserialize(ref reader, version) };
    }
}

using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public sealed record GetWorkingServersRequest(Guid ClusterId) : IRasRequest<GetWorkingServersResponse>
{
    public MessageType MessageType => MessageType.GetWorkingServersRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetWorkingServersRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetWorkingServersRequest(reader.ReadUuid());
    }
}

public sealed record GetWorkingServersResponse : IRasResponse<GetWorkingServersResponse>
{
    public IReadOnlyList<ServerInfo> Servers { get; init; } = Array.Empty<ServerInfo>();

    public MessageType MessageType => MessageType.GetWorkingServersResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Servers.Count);
        foreach (var server in Servers)
        {
            server.Serialize(ref writer, version);
        }
    }

    public static GetWorkingServersResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<ServerInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ServerInfo.Deserialize(ref reader, version));
        }
        return new GetWorkingServersResponse { Servers = list };
    }
}

public sealed record GetClusterManagersRequest(Guid ClusterId) : IRasRequest<GetClusterManagersResponse>
{
    public MessageType MessageType => MessageType.GetClusterManagersRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetClusterManagersRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetClusterManagersRequest(reader.ReadUuid());
    }
}

public sealed record GetClusterManagersResponse : IRasResponse<GetClusterManagersResponse>
{
    public IReadOnlyList<ManagerInfo> Managers { get; init; } = Array.Empty<ManagerInfo>();

    public MessageType MessageType => MessageType.GetClusterManagersResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Managers.Count);
        foreach (var mgr in Managers)
        {
            mgr.Serialize(ref writer, version);
        }
    }

    public static GetClusterManagersResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<ManagerInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ManagerInfo.Deserialize(ref reader, version));
        }
        return new GetClusterManagersResponse { Managers = list };
    }
}

public sealed record GetClusterServicesRequest(Guid ClusterId) : IRasRequest<GetClusterServicesResponse>
{
    public MessageType MessageType => MessageType.GetClusterServicesRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetClusterServicesRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetClusterServicesRequest(reader.ReadUuid());
    }
}

public sealed record GetClusterServicesResponse : IRasResponse<GetClusterServicesResponse>
{
    public IReadOnlyList<ServiceInfo> Services { get; init; } = Array.Empty<ServiceInfo>();

    public MessageType MessageType => MessageType.GetClusterServicesResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Services.Count);
        foreach (var svc in Services)
        {
            svc.Serialize(ref writer, version);
        }
    }

    public static GetClusterServicesResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<ServiceInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ServiceInfo.Deserialize(ref reader, version));
        }
        return new GetClusterServicesResponse { Services = list };
    }
}

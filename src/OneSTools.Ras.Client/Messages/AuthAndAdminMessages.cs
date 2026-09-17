using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public sealed record AuthenticateAgentRequest(string User, string Password) : IRasVoidRequest
{
    public MessageType MessageType => MessageType.AuthenticateAgentRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteString(User);
        writer.WriteString(Password);
    }
}

public sealed record AuthenticateRequest(Guid ClusterId, string User, string Password) : IRasVoidRequest
{
    public MessageType MessageType => MessageType.AuthenticateRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteString(User);
        writer.WriteString(Password);
    }
}

public sealed record AddAuthenticationRequest(Guid InfobaseId, string User, string Password) : IRasVoidRequest
{
    public MessageType MessageType => MessageType.AddAuthenticationRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(InfobaseId);
        writer.WriteString(User);
        writer.WriteString(Password);
    }
}

public sealed record GetAgentVersionRequest : IRasRequest<GetAgentVersionResponse>
{
    public MessageType MessageType => MessageType.GetAgentVersionRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
    }
}

public sealed record GetAgentVersionResponse : IRasResponse<GetAgentVersionResponse>
{
    public string Version { get; init; } = string.Empty;

    public MessageType MessageType => MessageType.GetAgentVersionResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteString(Version);
    }

    public static GetAgentVersionResponse Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetAgentVersionResponse { Version = reader.ReadString() ?? string.Empty };
    }
}

public sealed record GetAgentAdminsRequest : IRasRequest<GetAgentAdminsResponse>
{
    public MessageType MessageType => MessageType.GetAgentAdminsRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
    }
}

public sealed record GetAgentAdminsResponse : IRasResponse<GetAgentAdminsResponse>
{
    public IReadOnlyList<AdminInfo> Admins { get; init; } = Array.Empty<AdminInfo>();

    public MessageType MessageType => MessageType.GetAgentAdminsResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Admins.Count);
        foreach (var admin in Admins)
        {
            admin.Serialize(ref writer, version);
        }
    }

    public static GetAgentAdminsResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadSize();
        var list = new List<AdminInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(AdminInfo.Deserialize(ref reader, version));
        }
        return new GetAgentAdminsResponse { Admins = list };
    }
}

public sealed record GetClusterAdminsRequest(Guid ClusterId) : IRasRequest<GetClusterAdminsResponse>
{
    public MessageType MessageType => MessageType.GetClusterAdminsRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }
}

public sealed record GetClusterAdminsResponse : IRasResponse<GetClusterAdminsResponse>
{
    public IReadOnlyList<AdminInfo> Admins { get; init; } = Array.Empty<AdminInfo>();

    public MessageType MessageType => MessageType.GetClusterAdminsResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Admins.Count);
        foreach (var admin in Admins)
        {
            admin.Serialize(ref writer, version);
        }
    }

    public static GetClusterAdminsResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadSize();
        var list = new List<AdminInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(AdminInfo.Deserialize(ref reader, version));
        }
        return new GetClusterAdminsResponse { Admins = list };
    }
}

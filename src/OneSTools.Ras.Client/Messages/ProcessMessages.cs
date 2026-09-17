using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public sealed record GetWorkingProcessesRequest(Guid ClusterId) : IRasRequest<GetWorkingProcessesResponse>
{
    public MessageType MessageType => MessageType.GetWorkingProcessesRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetWorkingProcessesRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetWorkingProcessesRequest(reader.ReadUuid());
    }
}

public sealed record GetWorkingProcessesResponse : IRasResponse<GetWorkingProcessesResponse>
{
    public IReadOnlyList<ProcessInfo> Processes { get; init; } = Array.Empty<ProcessInfo>();

    public MessageType MessageType => MessageType.GetWorkingProcessesResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Processes.Count);
        foreach (var process in Processes)
        {
            process.Serialize(ref writer, version);
        }
    }

    public static GetWorkingProcessesResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadSize();
        var list = new List<ProcessInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ProcessInfo.Deserialize(ref reader, version));
        }
        return new GetWorkingProcessesResponse { Processes = list };
    }
}

public sealed record GetWorkingProcessInfoRequest(Guid ClusterId, Guid ProcessId) : IRasRequest<GetWorkingProcessInfoResponse>
{
    public MessageType MessageType => MessageType.GetWorkingProcessInfoRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(ProcessId);
    }

    public static GetWorkingProcessInfoRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetWorkingProcessInfoRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

public sealed record GetWorkingProcessInfoResponse : IRasResponse<GetWorkingProcessInfoResponse>
{
    public ProcessInfo Info { get; init; } = new();

    public MessageType MessageType => MessageType.GetWorkingProcessInfoResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        Info.Serialize(ref writer, version);
    }

    public static GetWorkingProcessInfoResponse Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetWorkingProcessInfoResponse { Info = ProcessInfo.Deserialize(ref reader, version) };
    }
}

using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

/// <summary>Запрос SWP-сообщение «GetLocksRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetLocksRequest(Guid ClusterId) : IRasRequest<GetLocksResponse>
{
    public MessageType MessageType => MessageType.GetLocksRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
    }

    public static GetLocksRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetLocksRequest(reader.ReadUuid());
    }
}

/// <summary>Ответ SWP-сообщение «GetLocksResponse». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetLocksResponse : IRasResponse<GetLocksResponse>
{
    public IReadOnlyList<LockInfo> Locks { get; init; } = Array.Empty<LockInfo>();

    public MessageType MessageType => MessageType.GetLocksResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Locks.Count);
        foreach (var lockInfo in Locks)
        {
            lockInfo.Serialize(ref writer, version);
        }
    }

    public static GetLocksResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<LockInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(LockInfo.Deserialize(ref reader, version));
        }
        return new GetLocksResponse { Locks = list };
    }
}

/// <summary>Запрос SWP-сообщение «GetInfoBaseLocksRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetInfoBaseLocksRequest(Guid ClusterId, Guid InfobaseId) : IRasRequest<GetInfoBaseLocksResponse>
{
    public MessageType MessageType => MessageType.GetInfoBaseLocksRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(InfobaseId);
    }

    public static GetInfoBaseLocksRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetInfoBaseLocksRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

/// <summary>Ответ SWP-сообщение «GetInfoBaseLocksResponse». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetInfoBaseLocksResponse : IRasResponse<GetInfoBaseLocksResponse>
{
    public IReadOnlyList<LockInfo> Locks { get; init; } = Array.Empty<LockInfo>();

    public MessageType MessageType => MessageType.GetInfoBaseLocksResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Locks.Count);
        foreach (var lockInfo in Locks)
        {
            lockInfo.Serialize(ref writer, version);
        }
    }

    public static GetInfoBaseLocksResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<LockInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(LockInfo.Deserialize(ref reader, version));
        }
        return new GetInfoBaseLocksResponse { Locks = list };
    }
}

/// <summary>Запрос SWP-сообщение «GetConnectionLocksRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetConnectionLocksRequest(Guid ClusterId, Guid ConnectionId) : IRasRequest<GetConnectionLocksResponse>
{
    public MessageType MessageType => MessageType.GetConnectionLocksRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(ConnectionId);
    }

    public static GetConnectionLocksRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetConnectionLocksRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

/// <summary>Ответ SWP-сообщение «GetConnectionLocksResponse». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetConnectionLocksResponse : IRasResponse<GetConnectionLocksResponse>
{
    public IReadOnlyList<LockInfo> Locks { get; init; } = Array.Empty<LockInfo>();

    public MessageType MessageType => MessageType.GetConnectionLocksResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Locks.Count);
        foreach (var lockInfo in Locks)
        {
            lockInfo.Serialize(ref writer, version);
        }
    }

    public static GetConnectionLocksResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<LockInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(LockInfo.Deserialize(ref reader, version));
        }
        return new GetConnectionLocksResponse { Locks = list };
    }
}

/// <summary>Запрос SWP-сообщение «GetSessionLocksRequest». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetSessionLocksRequest(Guid ClusterId, Guid SessionId) : IRasRequest<GetSessionLocksResponse>
{
    public MessageType MessageType => MessageType.GetSessionLocksRequest;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ClusterId);
        writer.WriteUuid(SessionId);
    }

    public static GetSessionLocksRequest Deserialize(ref RasReader reader, int version = 0)
    {
        return new GetSessionLocksRequest(reader.ReadUuid(), reader.ReadUuid());
    }
}

/// <summary>Ответ SWP-сообщение «GetSessionLocksResponse». Порядок/версионирование полей: github.com/v8platform/protos + docs/CODEC_SPEC.md, семантика — docs/API_REFERENCE.md.</summary>
public sealed record GetSessionLocksResponse : IRasResponse<GetSessionLocksResponse>
{
    public IReadOnlyList<LockInfo> Locks { get; init; } = Array.Empty<LockInfo>();

    public MessageType MessageType => MessageType.GetSessionLocksResponse;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteSize(Locks.Count);
        foreach (var lockInfo in Locks)
        {
            lockInfo.Serialize(ref writer, version);
        }
    }

    public static GetSessionLocksResponse Deserialize(ref RasReader reader, int version = 0)
    {
        int count = reader.ReadListCount();
        var list = new List<LockInfo>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(LockInfo.Deserialize(ref reader, version));
        }
        return new GetSessionLocksResponse { Locks = list };
    }
}

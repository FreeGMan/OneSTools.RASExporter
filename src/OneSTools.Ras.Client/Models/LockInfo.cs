using System;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>Модель данных «LockInfo» (docs/API_REFERENCE.md §3; версионированные поля — по reference/v8platform-protos).</summary>
public sealed record LockInfo
{
    public Guid ConnectionId { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime LockedAt { get; init; }
    public Guid ObjectId { get; init; }
    public Guid SessionId { get; init; }

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(ConnectionId);
        writer.WriteString(Description);
        writer.WriteTime(LockedAt);
        writer.WriteUuid(ObjectId);
        writer.WriteUuid(SessionId);
    }

    public static LockInfo Deserialize(ref RasReader reader, int version = 0)
    {
        return new LockInfo
        {
            ConnectionId = reader.ReadUuid(),
            Description = reader.ReadString() ?? string.Empty,
            LockedAt = reader.ReadTime(),
            ObjectId = reader.ReadUuid(),
            SessionId = reader.ReadUuid(),
        };
    }
}

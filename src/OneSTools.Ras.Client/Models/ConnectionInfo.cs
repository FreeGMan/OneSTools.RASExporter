using System;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>Модель данных «ConnectionInfo» (docs/API_REFERENCE.md §3; версионированные поля — по reference/v8platform-protos).</summary>
public sealed record ConnectionInfo
{
    public Guid Uuid { get; init; }
    public string Application { get; init; } = string.Empty;
    public int BlockedByLs { get; init; }
    public DateTime ConnectedAt { get; init; }
    public int Id { get; init; }
    public string Host { get; init; } = string.Empty;
    public Guid InfobaseId { get; init; }
    public Guid ProcessId { get; init; }
    public int SessionId { get; init; }

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteString(Application);
        writer.WriteInt32(BlockedByLs);
        writer.WriteTime(ConnectedAt);
        writer.WriteInt32(Id);
        writer.WriteString(Host);
        writer.WriteUuid(InfobaseId);
        writer.WriteUuid(ProcessId);
        writer.WriteInt32(SessionId);
    }

    public static ConnectionInfo Deserialize(ref RasReader reader, int version = 0)
    {
        return new ConnectionInfo
        {
            Uuid = reader.ReadUuid(),
            Application = reader.ReadString() ?? string.Empty,
            BlockedByLs = reader.ReadInt32(),
            ConnectedAt = reader.ReadTime(),
            Id = reader.ReadInt32(),
            Host = reader.ReadString() ?? string.Empty,
            InfobaseId = reader.ReadUuid(),
            ProcessId = reader.ReadUuid(),
            SessionId = reader.ReadInt32(),
        };
    }
}

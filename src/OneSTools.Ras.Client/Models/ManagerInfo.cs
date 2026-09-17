using System;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

public sealed record ManagerInfo
{
    public Guid Uuid { get; init; }
    public string Descr { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int MainManager { get; init; }
    public short Port { get; init; }
    public string Pid { get; init; } = string.Empty;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteString(Descr);
        writer.WriteString(Host);
        writer.WriteInt32(MainManager);
        writer.WriteShort(Port);
        writer.WriteString(Pid);
    }

    public static ManagerInfo Deserialize(ref RasReader reader, int version = 0)
    {
        return new ManagerInfo
        {
            Uuid = reader.ReadUuid(),
            Descr = reader.ReadString() ?? string.Empty,
            Host = reader.ReadString() ?? string.Empty,
            MainManager = reader.ReadInt32(),
            Port = reader.ReadShort(),
            Pid = reader.ReadString() ?? string.Empty,
        };
    }
}

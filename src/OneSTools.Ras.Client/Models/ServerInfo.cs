using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

public readonly record struct PortRange(short High, short Low)
{
    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteShort(High);
        writer.WriteShort(Low);
    }

    public static PortRange Deserialize(ref RasReader reader, int version = 0)
    {
        return new PortRange(reader.ReadShort(), reader.ReadShort());
    }
}

public sealed record ServerInfo
{
    public Guid Uuid { get; init; }
    public string AgentHost { get; init; } = string.Empty;
    public short AgentPort { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool MainServer { get; init; }
    public long SafeWorkingProcessesMemoryLimit { get; init; }
    public long SafeCallMemoryLimit { get; init; }
    public int InfobasesLimit { get; init; }
    public long MemoryLimit { get; init; }
    public int ConnectionsLimit { get; init; }
    public short ClusterPort { get; init; }
    public bool DedicateManagers { get; init; }
    public IReadOnlyList<PortRange> PortRanges { get; init; } = Array.Empty<PortRange>();
    public long CriticalTotalMemory { get; init; }
    public long TemporaryAllowedTotalMemory { get; init; }
    public long TemporaryAllowedTotalMemoryTimeLimit { get; init; }

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteString(AgentHost);
        writer.WriteShort(AgentPort);
        writer.WriteString(Name);
        writer.WriteBool(MainServer);
        writer.WriteLong(SafeWorkingProcessesMemoryLimit);
        writer.WriteLong(SafeCallMemoryLimit);
        writer.WriteInt32(InfobasesLimit);
        writer.WriteLong(MemoryLimit);
        writer.WriteInt32(ConnectionsLimit);
        writer.WriteShort(ClusterPort);
        writer.WriteBool(DedicateManagers);

        writer.WriteSize(PortRanges.Count);
        foreach (var range in PortRanges)
        {
            range.Serialize(ref writer, version);
        }

        if (version >= 8)
        {
            writer.WriteLong(CriticalTotalMemory);
            writer.WriteLong(TemporaryAllowedTotalMemory);
            writer.WriteLong(TemporaryAllowedTotalMemoryTimeLimit);
        }
    }

    public static ServerInfo Deserialize(ref RasReader reader, int version = 0)
    {
        var server = new ServerInfo
        {
            Uuid = reader.ReadUuid(),
            AgentHost = reader.ReadString() ?? string.Empty,
            AgentPort = reader.ReadShort(),
            Name = reader.ReadString() ?? string.Empty,
            MainServer = reader.ReadBool(),
            SafeWorkingProcessesMemoryLimit = reader.ReadLong(),
            SafeCallMemoryLimit = reader.ReadLong(),
            InfobasesLimit = reader.ReadInt32(),
            MemoryLimit = reader.ReadLong(),
            ConnectionsLimit = reader.ReadInt32(),
            ClusterPort = reader.ReadShort(),
            DedicateManagers = reader.ReadBool(),
        };

        int rangeCount = reader.ReadSize();
        var ranges = new List<PortRange>(rangeCount);
        for (int i = 0; i < rangeCount; i++)
        {
            ranges.Add(PortRange.Deserialize(ref reader, version));
        }

        server = server with { PortRanges = ranges };

        if (version >= 8 && reader.Remaining > 0)
        {
            server = server with
            {
                CriticalTotalMemory = reader.ReadLong(),
                TemporaryAllowedTotalMemory = reader.ReadLong(),
                TemporaryAllowedTotalMemoryTimeLimit = reader.ReadLong(),
            };
        }

        return server;
    }
}

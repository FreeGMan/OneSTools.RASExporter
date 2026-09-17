using System;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

public sealed record ClusterInfo
{
    public Guid Uuid { get; init; }
    public int ExpirationTimeout { get; init; }
    public string Host { get; init; } = string.Empty;
    public int LifetimeLimit { get; init; }
    public short Port { get; init; }
    public int MaxMemorySize { get; init; }
    public int MaxMemoryTimeLimit { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SecurityLevel { get; init; }
    public int SessionFaultToleranceLevel { get; init; }
    public int LoadBalancingMode { get; init; }
    public int ErrorsCountThreshold { get; init; }
    public bool KillProblemProcesses { get; init; }
    public bool KillByMemoryWithDump { get; init; }

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteInt32(ExpirationTimeout);
        writer.WriteString(Host);
        writer.WriteInt32(LifetimeLimit);
        writer.WriteShort(Port);
        writer.WriteInt32(MaxMemorySize);
        writer.WriteInt32(MaxMemoryTimeLimit);
        writer.WriteString(Name);
        writer.WriteInt32(SecurityLevel);
        writer.WriteInt32(SessionFaultToleranceLevel);
        writer.WriteInt32(LoadBalancingMode);
        writer.WriteInt32(ErrorsCountThreshold);
        writer.WriteBool(KillProblemProcesses);
        if (version >= 9)
        {
            writer.WriteBool(KillByMemoryWithDump);
        }
    }

    public static ClusterInfo Deserialize(ref RasReader reader, int version = 0)
    {
        var cluster = new ClusterInfo
        {
            Uuid = reader.ReadUuid(),
            ExpirationTimeout = reader.ReadInt32(),
            Host = reader.ReadString() ?? string.Empty,
            LifetimeLimit = reader.ReadInt32(),
            Port = reader.ReadShort(),
            MaxMemorySize = reader.ReadInt32(),
            MaxMemoryTimeLimit = reader.ReadInt32(),
            Name = reader.ReadString() ?? string.Empty,
            SecurityLevel = reader.ReadInt32(),
            SessionFaultToleranceLevel = reader.ReadInt32(),
            LoadBalancingMode = reader.ReadInt32(),
            ErrorsCountThreshold = reader.ReadInt32(),
            KillProblemProcesses = reader.ReadBool(),
        };

        if (version >= 9 && reader.Remaining > 0)
        {
            return cluster with { KillByMemoryWithDump = reader.ReadBool() };
        }

        return cluster;
    }
}

using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>Модель данных «ProcessInfo» (docs/API_REFERENCE.md §3; версионированные поля — по github.com/v8platform/protos).</summary>
public sealed record ProcessInfo
{
    public Guid Uuid { get; init; }
    public double AvgBackCallTime { get; init; }
    public double AvgCallTime { get; init; }
    public double AvgDbCallTime { get; init; }
    public double AvgLockCallTime { get; init; }
    public double AvgServerCallTime { get; init; }
    public double AvgThreads { get; init; }
    public int Capacity { get; init; }
    public int Connections { get; init; }
    public string Host { get; init; } = string.Empty;
    public bool Enable { get; init; }
    public IReadOnlyList<LicenseInfo> Licenses { get; init; } = Array.Empty<LicenseInfo>();
    public short Port { get; init; }
    public int MemoryExcessTime { get; init; }
    public int MemorySize { get; init; }
    public string Pid { get; init; } = string.Empty;
    public bool Running { get; init; }
    public int SelectionSize { get; init; }
    public DateTime StartedAt { get; init; }
    public bool Use { get; init; }
    public int AvailablePerformance { get; init; }
    public bool Reverse { get; init; }

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteDouble(AvgBackCallTime);
        writer.WriteDouble(AvgCallTime);
        writer.WriteDouble(AvgDbCallTime);
        writer.WriteDouble(AvgLockCallTime);
        writer.WriteDouble(AvgServerCallTime);
        writer.WriteDouble(AvgThreads);
        writer.WriteInt32(Capacity);
        writer.WriteInt32(Connections);
        writer.WriteString(Host);
        writer.WriteBool(Enable);

        writer.WriteSize(Licenses.Count);
        foreach (var lic in Licenses)
        {
            lic.Serialize(ref writer, version);
        }

        writer.WriteShort(Port);
        writer.WriteInt32(MemoryExcessTime);
        writer.WriteInt32(MemorySize);
        writer.WriteString(Pid);
        writer.WriteInt32(Running ? 1 : 0);
        writer.WriteInt32(SelectionSize);
        writer.WriteTime(StartedAt);
        writer.WriteInt32(Use ? 1 : 0);
        writer.WriteInt32(AvailablePerformance);

        if (version >= 9)
        {
            writer.WriteBool(Reverse);
        }
    }

    public static ProcessInfo Deserialize(ref RasReader reader, int version = 0)
    {
        var process = new ProcessInfo
        {
            Uuid = reader.ReadUuid(),
            AvgBackCallTime = reader.ReadDouble(),
            AvgCallTime = reader.ReadDouble(),
            AvgDbCallTime = reader.ReadDouble(),
            AvgLockCallTime = reader.ReadDouble(),
            AvgServerCallTime = reader.ReadDouble(),
            AvgThreads = reader.ReadDouble(),
            Capacity = reader.ReadInt32(),
            Connections = reader.ReadInt32(),
            Host = reader.ReadString() ?? string.Empty,
            Enable = reader.ReadBool(),
        };

        int licCount = reader.ReadListCount();
        var licenses = new List<LicenseInfo>(licCount);
        for (int i = 0; i < licCount; i++)
        {
            licenses.Add(LicenseInfo.Deserialize(ref reader, version));
        }

        process = process with
        {
            Licenses = licenses,
            Port = reader.ReadShort(),
            MemoryExcessTime = reader.ReadInt32(),
            MemorySize = reader.ReadInt32(),
            Pid = reader.ReadString() ?? string.Empty,
            Running = reader.ReadInt32() != 0,
            SelectionSize = reader.ReadInt32(),
            StartedAt = reader.ReadTime(),
            Use = reader.ReadInt32() != 0,
            AvailablePerformance = reader.ReadInt32(),
        };

        if (version >= 9 && reader.Remaining > 0)
        {
            process = process with { Reverse = reader.ReadBool() };
        }

        return process;
    }
}

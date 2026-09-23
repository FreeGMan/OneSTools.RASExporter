using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>Модель данных «ServiceInfo» (docs/API_REFERENCE.md §3; версионированные поля — по reference/v8platform-protos).</summary>
public sealed record ServiceInfo
{
    public string Name { get; init; } = string.Empty;
    public string Descr { get; init; } = string.Empty;
    public int MainOnly { get; init; }
    public IReadOnlyList<Guid> Managers { get; init; } = Array.Empty<Guid>();

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteString(Name);
        writer.WriteString(Descr);
        writer.WriteInt32(MainOnly);
        
        writer.WriteSize(Managers.Count);
        foreach (var mgr in Managers)
        {
            writer.WriteUuid(mgr);
        }
    }

    public static ServiceInfo Deserialize(ref RasReader reader, int version = 0)
    {
        var name = reader.ReadString() ?? string.Empty;
        var descr = reader.ReadString() ?? string.Empty;
        var mainOnly = reader.ReadInt32();

        int count = reader.ReadSize();
        var managers = new List<Guid>(count);
        for (int i = 0; i < count; i++)
        {
            managers.Add(reader.ReadUuid());
        }

        return new ServiceInfo
        {
            Name = name,
            Descr = descr,
            MainOnly = mainOnly,
            Managers = managers,
        };
    }
}

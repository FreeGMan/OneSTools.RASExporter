using System;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

public sealed record InfobaseSummaryInfo
{
    public Guid Uuid { get; init; }
    public string Descr { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteString(Descr);
        writer.WriteString(Name);
    }

    public static InfobaseSummaryInfo Deserialize(ref RasReader reader, int version = 0)
    {
        return new InfobaseSummaryInfo
        {
            Uuid = reader.ReadUuid(),
            Descr = reader.ReadString() ?? string.Empty,
            Name = reader.ReadString() ?? string.Empty,
        };
    }
}

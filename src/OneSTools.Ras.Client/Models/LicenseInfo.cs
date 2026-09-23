using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>Модель данных «LicenseInfo» (docs/API_REFERENCE.md §3; версионированные поля — по reference/v8platform-protos).</summary>
public sealed record LicenseInfo
{
    public string FullName { get; init; } = string.Empty;
    public string FullPresentation { get; init; } = string.Empty;
    public bool IssuedByServer { get; init; }
    public int LicenseType { get; init; }
    public int MaxUsersAll { get; init; }
    public int MaxUsersCur { get; init; }
    public bool Net { get; init; }
    public string RmngrAddress { get; init; } = string.Empty;
    public string RmngrPid { get; init; } = string.Empty;
    public int RmngrPort { get; init; }
    public string Series { get; init; } = string.Empty;
    public string ShortPresentation { get; init; } = string.Empty;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteString(FullName);
        writer.WriteString(FullPresentation);
        writer.WriteBool(IssuedByServer);
        writer.WriteInt32(LicenseType);
        writer.WriteInt32(MaxUsersAll);
        writer.WriteInt32(MaxUsersCur);
        writer.WriteBool(Net);
        writer.WriteString(RmngrAddress);
        writer.WriteString(RmngrPid);
        writer.WriteInt32(RmngrPort);
        writer.WriteString(Series);
        writer.WriteString(ShortPresentation);
    }

    public static LicenseInfo Deserialize(ref RasReader reader, int version = 0)
    {
        return new LicenseInfo
        {
            FullName = reader.ReadString() ?? string.Empty,
            FullPresentation = reader.ReadString() ?? string.Empty,
            IssuedByServer = reader.ReadBool(),
            LicenseType = reader.ReadInt32(),
            MaxUsersAll = reader.ReadInt32(),
            MaxUsersCur = reader.ReadInt32(),
            Net = reader.ReadBool(),
            RmngrAddress = reader.ReadString() ?? string.Empty,
            RmngrPid = reader.ReadString() ?? string.Empty,
            RmngrPort = reader.ReadInt32(),
            Series = reader.ReadString() ?? string.Empty,
            ShortPresentation = reader.ReadString() ?? string.Empty,
        };
    }
}

using System;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>
/// Модель данных «InfobaseInfo» (docs/API_REFERENCE.md §3).
/// Порядок полей — позиционный по v8platform/protos (до order 24); хвостовые версионные поля
/// (order 25–28) подтверждены живым стендом RAS 8.3.25.1520 (замер длин ответов v3.0–14.0):
/// ReserveWorkingProcesses v≥9, DisableLocalSpeechToText v≥13, три целых v≥14.
/// </summary>
public sealed record InfobaseInfo
{
    public Guid Uuid { get; init; }
    public int DateOffset { get; init; }
    public string Dbms { get; init; } = string.Empty;
    public string DbName { get; init; } = string.Empty;
    public string DbPwd { get; init; } = string.Empty;
    public string DbServer { get; init; } = string.Empty;
    public string DbUser { get; init; } = string.Empty;
    public DateTime DeniedFrom { get; init; }
    public string DeniedMessage { get; init; } = string.Empty;
    public string DeniedParameter { get; init; } = string.Empty;
    public DateTime DeniedTo { get; init; }
    public string Descr { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PermissionCode { get; init; } = string.Empty;
    public bool ScheduledJobsDeny { get; init; }
    public int SecurityLevel { get; init; }
    public bool SessionsDeny { get; init; }
    public int LicenseDistribution { get; init; }
    public string ExternalSessionManagerConnectionString { get; init; } = string.Empty;
    public bool ExternalSessionManagerRequired { get; init; }
    public string SecurityProfileName { get; init; } = string.Empty;
    public string SafeModeSecurityProfileName { get; init; } = string.Empty;
    public bool ReserveWorkingProcesses { get; init; }
    public bool DisableLocalSpeechToText { get; init; }
    public int ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers { get; init; }
    public int MinimumScheduledJobsStartPeriodWithoutActiveUsers { get; init; }
    public int MaximumScheduledJobsStartShiftWithoutActiveUsers { get; init; }

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteInt32(DateOffset);
        writer.WriteString(Dbms);
        writer.WriteString(DbName);
        writer.WriteString(DbPwd);
        writer.WriteString(DbServer);
        writer.WriteString(DbUser);
        writer.WriteTime(DeniedFrom);
        writer.WriteString(DeniedMessage);
        writer.WriteString(DeniedParameter);
        writer.WriteTime(DeniedTo);
        writer.WriteString(Descr);
        writer.WriteString(Locale);
        writer.WriteString(Name);
        writer.WriteString(PermissionCode);
        writer.WriteBool(ScheduledJobsDeny);
        writer.WriteInt32(SecurityLevel);
        writer.WriteBool(SessionsDeny);
        writer.WriteInt32(LicenseDistribution);
        writer.WriteString(ExternalSessionManagerConnectionString);
        writer.WriteBool(ExternalSessionManagerRequired);
        writer.WriteString(SecurityProfileName);
        writer.WriteString(SafeModeSecurityProfileName);
        if (version >= 9)
        {
            writer.WriteBool(ReserveWorkingProcesses);
        }
        if (version >= 13)
        {
            writer.WriteBool(DisableLocalSpeechToText);
        }
        if (version >= 14)
        {
            writer.WriteInt32(ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers);
            writer.WriteInt32(MinimumScheduledJobsStartPeriodWithoutActiveUsers);
            writer.WriteInt32(MaximumScheduledJobsStartShiftWithoutActiveUsers);
        }
    }

    public static InfobaseInfo Deserialize(ref RasReader reader, int version = 0)
    {
        var info = new InfobaseInfo
        {
            Uuid = reader.ReadUuid(),
            DateOffset = reader.ReadInt32(),
            Dbms = reader.ReadString() ?? string.Empty,
            DbName = reader.ReadString() ?? string.Empty,
            DbPwd = reader.ReadString() ?? string.Empty,
            DbServer = reader.ReadString() ?? string.Empty,
            DbUser = reader.ReadString() ?? string.Empty,
            DeniedFrom = reader.ReadTime(),
            DeniedMessage = reader.ReadString() ?? string.Empty,
            DeniedParameter = reader.ReadString() ?? string.Empty,
            DeniedTo = reader.ReadTime(),
            Descr = reader.ReadString() ?? string.Empty,
            Locale = reader.ReadString() ?? string.Empty,
            Name = reader.ReadString() ?? string.Empty,
            PermissionCode = reader.ReadString() ?? string.Empty,
            ScheduledJobsDeny = reader.ReadBool(),
            SecurityLevel = reader.ReadInt32(),
            SessionsDeny = reader.ReadBool(),
            LicenseDistribution = reader.ReadInt32(),
            ExternalSessionManagerConnectionString = reader.ReadString() ?? string.Empty,
            ExternalSessionManagerRequired = reader.ReadBool(),
            SecurityProfileName = reader.ReadString() ?? string.Empty,
            SafeModeSecurityProfileName = reader.ReadString() ?? string.Empty,
        };

        if (version >= 9)
        {
            info = info with { ReserveWorkingProcesses = reader.ReadBool() };
        }
        if (version >= 13)
        {
            info = info with { DisableLocalSpeechToText = reader.ReadBool() };
        }
        if (version >= 14)
        {
            info = info with
            {
                ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers = reader.ReadInt32(),
                MinimumScheduledJobsStartPeriodWithoutActiveUsers = reader.ReadInt32(),
                MaximumScheduledJobsStartShiftWithoutActiveUsers = reader.ReadInt32(),
            };
        }

        return info;
    }
}

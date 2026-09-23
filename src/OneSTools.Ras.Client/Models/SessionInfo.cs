using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

/// <summary>Модель данных «SessionInfo» (docs/API_REFERENCE.md §3; версионированные поля — по reference/v8platform-protos).</summary>
public sealed record SessionInfo
{
    public Guid Uuid { get; init; }
    public string AppId { get; init; } = string.Empty;
    public int BlockedByDbms { get; init; }
    public int BlockedByLs { get; init; }
    public long BytesAll { get; init; }
    public long BytesLast5Min { get; init; }
    public int CallsAll { get; init; }
    public long CallsLast5Min { get; init; }
    public Guid ConnectionId { get; init; }
    public long DbmsBytesAll { get; init; }
    public long DbmsBytesLast5Min { get; init; }
    public string DbProcInfo { get; init; } = string.Empty;
    public int DbProcTook { get; init; }
    public DateTime DbProcTookAt { get; init; }
    public int DurationAll { get; init; }
    public int DurationAllDbms { get; init; }
    public int DurationCurrent { get; init; }
    public int DurationCurrentDbms { get; init; }
    public long DurationLast5Min { get; init; }
    public long DurationLast5MinDbms { get; init; }
    public string Host { get; init; } = string.Empty;
    public Guid InfobaseId { get; init; }
    public DateTime LastActiveAt { get; init; }
    public bool Hibernate { get; init; }
    public int PassiveSessionHibernateTime { get; init; }
    public int HibernateSessionTerminateTime { get; init; }
    public IReadOnlyList<LicenseInfo> Licenses { get; init; } = Array.Empty<LicenseInfo>();
    public string Locale { get; init; } = string.Empty;
    public Guid ProcessId { get; init; }
    public int Id { get; init; }
    public DateTime StartedAt { get; init; }
    public string UserName { get; init; } = string.Empty;

    // version >= 4
    public long MemoryCurrent { get; init; }
    public long MemoryLast5Min { get; init; }
    public long MemoryTotal { get; init; }
    public long ReadCurrent { get; init; }
    public long ReadLast5Min { get; init; }
    public long ReadTotal { get; init; }
    public long WriteCurrent { get; init; }
    public long WriteLast5Min { get; init; }
    public long WriteTotal { get; init; }

    // version >= 5
    public int DurationCurrentService { get; init; }
    public long DurationLast5MinService { get; init; }
    public int DurationAllService { get; init; }
    public string CurrentServiceName { get; init; } = string.Empty;

    // version >= 6
    public long CpuTimeCurrent { get; init; }
    public long CpuTimeLast5Min { get; init; }
    public long CpuTimeTotal { get; init; }

    // version >= 7
    public string DataSeparation { get; init; } = string.Empty;

    // version >= 10
    public string ClientIpAddress { get; init; } = string.Empty;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteUuid(Uuid);
        writer.WriteString(AppId);
        writer.WriteInt32(BlockedByDbms);
        writer.WriteInt32(BlockedByLs);
        writer.WriteLong(BytesAll);
        writer.WriteLong(BytesLast5Min);
        writer.WriteInt32(CallsAll);
        writer.WriteLong(CallsLast5Min);
        writer.WriteUuid(ConnectionId);
        writer.WriteLong(DbmsBytesAll);
        writer.WriteLong(DbmsBytesLast5Min);
        writer.WriteString(DbProcInfo);
        writer.WriteInt32(DbProcTook);
        writer.WriteTime(DbProcTookAt);
        writer.WriteInt32(DurationAll);
        writer.WriteInt32(DurationAllDbms);
        writer.WriteInt32(DurationCurrent);
        writer.WriteInt32(DurationCurrentDbms);
        writer.WriteLong(DurationLast5Min);
        writer.WriteLong(DurationLast5MinDbms);
        writer.WriteString(Host);
        writer.WriteUuid(InfobaseId);
        writer.WriteTime(LastActiveAt);
        writer.WriteBool(Hibernate);
        writer.WriteInt32(PassiveSessionHibernateTime);
        writer.WriteInt32(HibernateSessionTerminateTime);
        
        writer.WriteSize(Licenses.Count);
        foreach (var lic in Licenses)
        {
            lic.Serialize(ref writer, version);
        }

        writer.WriteString(Locale);
        writer.WriteUuid(ProcessId);
        writer.WriteInt32(Id);
        writer.WriteTime(StartedAt);
        writer.WriteString(UserName);

        if (version >= 4)
        {
            writer.WriteLong(MemoryCurrent);
            writer.WriteLong(MemoryLast5Min);
            writer.WriteLong(MemoryTotal);
            writer.WriteLong(ReadCurrent);
            writer.WriteLong(ReadLast5Min);
            writer.WriteLong(ReadTotal);
            writer.WriteLong(WriteCurrent);
            writer.WriteLong(WriteLast5Min);
            writer.WriteLong(WriteTotal);
        }

        if (version >= 5)
        {
            writer.WriteInt32(DurationCurrentService);
            writer.WriteLong(DurationLast5MinService);
            writer.WriteInt32(DurationAllService);
            writer.WriteString(CurrentServiceName);
        }

        if (version >= 6)
        {
            writer.WriteLong(CpuTimeCurrent);
            writer.WriteLong(CpuTimeLast5Min);
            writer.WriteLong(CpuTimeTotal);
        }

        if (version >= 7)
        {
            writer.WriteString(DataSeparation);
        }

        if (version >= 10)
        {
            writer.WriteString(ClientIpAddress);
        }
    }

    public static SessionInfo Deserialize(ref RasReader reader, int version = 0)
    {
        var session = new SessionInfo
        {
            Uuid = reader.ReadUuid(),
            AppId = reader.ReadString() ?? string.Empty,
            BlockedByDbms = reader.ReadInt32(),
            BlockedByLs = reader.ReadInt32(),
            BytesAll = reader.ReadLong(),
            BytesLast5Min = reader.ReadLong(),
            CallsAll = reader.ReadInt32(),
            CallsLast5Min = reader.ReadLong(),
            ConnectionId = reader.ReadUuid(),
            DbmsBytesAll = reader.ReadLong(),
            DbmsBytesLast5Min = reader.ReadLong(),
            DbProcInfo = reader.ReadString() ?? string.Empty,
            DbProcTook = reader.ReadInt32(),
            DbProcTookAt = reader.ReadTime(),
            DurationAll = reader.ReadInt32(),
            DurationAllDbms = reader.ReadInt32(),
            DurationCurrent = reader.ReadInt32(),
            DurationCurrentDbms = reader.ReadInt32(),
            DurationLast5Min = reader.ReadLong(),
            DurationLast5MinDbms = reader.ReadLong(),
            Host = reader.ReadString() ?? string.Empty,
            InfobaseId = reader.ReadUuid(),
            LastActiveAt = reader.ReadTime(),
            Hibernate = reader.ReadBool(),
            PassiveSessionHibernateTime = reader.ReadInt32(),
            HibernateSessionTerminateTime = reader.ReadInt32(),
        };

        int licCount = reader.ReadSize();
        var licenses = new List<LicenseInfo>(licCount);
        for (int i = 0; i < licCount; i++)
        {
            licenses.Add(LicenseInfo.Deserialize(ref reader, version));
        }

        session = session with
        {
            Licenses = licenses,
            Locale = reader.ReadString() ?? string.Empty,
            ProcessId = reader.ReadUuid(),
            Id = reader.ReadInt32(),
            StartedAt = reader.ReadTime(),
            UserName = reader.ReadString() ?? string.Empty,
        };

        if (version >= 4 && reader.Remaining > 0)
        {
            session = session with
            {
                MemoryCurrent = reader.ReadLong(),
                MemoryLast5Min = reader.ReadLong(),
                MemoryTotal = reader.ReadLong(),
                ReadCurrent = reader.ReadLong(),
                ReadLast5Min = reader.ReadLong(),
                ReadTotal = reader.ReadLong(),
                WriteCurrent = reader.ReadLong(),
                WriteLast5Min = reader.ReadLong(),
                WriteTotal = reader.ReadLong(),
            };
        }

        if (version >= 5 && reader.Remaining > 0)
        {
            session = session with
            {
                DurationCurrentService = reader.ReadInt32(),
                DurationLast5MinService = reader.ReadLong(),
                DurationAllService = reader.ReadInt32(),
                CurrentServiceName = reader.ReadString() ?? string.Empty,
            };
        }

        if (version >= 6 && reader.Remaining > 0)
        {
            session = session with
            {
                CpuTimeCurrent = reader.ReadLong(),
                CpuTimeLast5Min = reader.ReadLong(),
                CpuTimeTotal = reader.ReadLong(),
            };
        }

        if (version >= 7 && reader.Remaining > 0)
        {
            session = session with
            {
                DataSeparation = reader.ReadString() ?? string.Empty,
            };
        }

        if (version >= 10 && reader.Remaining > 0)
        {
            session = session with
            {
                ClientIpAddress = reader.ReadString() ?? string.Empty,
            };
        }

        return session;
    }
}

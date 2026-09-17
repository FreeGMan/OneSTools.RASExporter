using System;
using System.Buffers;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Messages;

public sealed class SessionMessagesTests
{
    [Fact]
    public void GetSessionsResponse_With_Licenses_And_Extended_Fields_Roundtrips()
    {
        var license = new LicenseInfo
        {
            FullName = "1C:Enterprise 8 Server License",
            FullPresentation = "1C Server (x64) License #12345",
            IssuedByServer = true,
            LicenseType = 1,
            MaxUsersAll = 100,
            MaxUsersCur = 25,
            Net = true,
            RmngrAddress = "192.168.1.10",
            RmngrPid = "1234",
            RmngrPort = 1560,
            Series = "SERIES-1C",
            ShortPresentation = "Server 100 users"
        };

        var session = new SessionInfo
        {
            Uuid = Guid.NewGuid(),
            AppId = "1CV8C",
            BlockedByDbms = 0,
            BlockedByLs = 0,
            BytesAll = 1048576,
            BytesLast5Min = 524288,
            CallsAll = 120,
            CallsLast5Min = 30,
            ConnectionId = Guid.NewGuid(),
            DbmsBytesAll = 2048576,
            DbmsBytesLast5Min = 102400,
            DbProcInfo = "SELECT * FROM V8USERS",
            DbProcTook = 15,
            DbProcTookAt = DateTime.UtcNow,
            DurationAll = 5000,
            DurationAllDbms = 1200,
            DurationCurrent = 10,
            DurationCurrentDbms = 2,
            DurationLast5Min = 300,
            DurationLast5MinDbms = 50,
            Host = "client-pc-01",
            InfobaseId = Guid.NewGuid(),
            LastActiveAt = DateTime.UtcNow,
            Hibernate = false,
            PassiveSessionHibernateTime = 1200,
            HibernateSessionTerminateTime = 86400,
            Licenses = new[] { license },
            Locale = "ru_RU",
            ProcessId = Guid.NewGuid(),
            Id = 42,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            UserName = "Ivanov",
            MemoryCurrent = 128 * 1024 * 1024,
            MemoryLast5Min = 64 * 1024 * 1024,
            MemoryTotal = 256 * 1024 * 1024,
            ReadCurrent = 1000,
            ReadLast5Min = 500,
            ReadTotal = 15000,
            WriteCurrent = 200,
            WriteLast5Min = 100,
            WriteTotal = 3000,
            DurationCurrentService = 5,
            DurationLast5MinService = 50,
            DurationAllService = 100,
            CurrentServiceName = "SessionService",
            CpuTimeCurrent = 25,
            CpuTimeLast5Min = 150,
            CpuTimeTotal = 1000,
            DataSeparation = "0",
            ClientIpAddress = "192.168.1.100"
        };

        var response = new GetSessionsResponse
        {
            Sessions = new[] { session }
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        response.Serialize(ref writer, version: 10);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetSessionsResponse.Deserialize(ref reader, version: 10);

        Assert.Single(deserialized.Sessions);
        var res = deserialized.Sessions[0];

        Assert.Equal(session.Uuid, res.Uuid);
        Assert.Equal("Ivanov", res.UserName);
        Assert.Equal("1CV8C", res.AppId);
        Assert.Equal(42, res.Id);
        Assert.Single(res.Licenses);
        Assert.Equal("1C Server (x64) License #12345", res.Licenses[0].FullPresentation);
        Assert.Equal(128 * 1024 * 1024, res.MemoryCurrent);
        Assert.Equal("SessionService", res.CurrentServiceName);
        Assert.Equal(25, res.CpuTimeCurrent);
        Assert.Equal("0", res.DataSeparation);
        Assert.Equal("192.168.1.100", res.ClientIpAddress);
    }
}

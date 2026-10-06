using System;
using System.Buffers;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Messages;

public sealed class InfobaseMessagesTests
{
    [Fact]
    public void GetInfoBasesRequest_Roundtrips()
    {
        var clusterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        new GetInfoBasesRequest(clusterId).Serialize(ref writer);

        var reader = new RasReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        var request = GetInfoBasesRequest.Deserialize(ref reader);

        Assert.Equal(clusterId, request.ClusterId);
        Assert.Equal(MessageType.GetInfoBasesRequest, request.MessageType);
    }

    [Fact]
    public void GetInfoBasesResponse_Empty_List_Roundtrips()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        new GetInfoBasesResponse().Serialize(ref writer, 14);

        var reader = new RasReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        Assert.Empty(GetInfoBasesResponse.Deserialize(ref reader, 14).InfoBases);
    }

    [Fact]
    public void GetInfoBasesResponse_Roundtrips_All_Fields_At_Version_14()
    {
        var deniedFrom = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var deniedTo = new DateTime(2026, 9, 2, 12, 30, 0, DateTimeKind.Utc);
        var infobase = new InfobaseInfo
        {
            Uuid = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DateOffset = 180,
            Dbms = "PostgreSQL",
            DbName = "erp",
            DbPwd = "S3cret",
            DbServer = "db-01.internal",
            DbUser = "postgres",
            DeniedFrom = deniedFrom,
            DeniedMessage = "Обновление конфигурации",
            DeniedParameter = "update",
            DeniedTo = deniedTo,
            Descr = "ERP",
            Locale = "ru_RU",
            Name = "erp_prod",
            PermissionCode = "PC-1",
            ScheduledJobsDeny = true,
            SecurityLevel = 1,
            SessionsDeny = true,
            LicenseDistribution = 0,
            ExternalSessionManagerConnectionString = "Host=esm;Port=1;UID=u;Pwd=p",
            ExternalSessionManagerRequired = true,
            SecurityProfileName = "Default",
            SafeModeSecurityProfileName = "SafeMode",
            ReserveWorkingProcesses = true,
            DisableLocalSpeechToText = true,
            ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers = 42,
            MinimumScheduledJobsStartPeriodWithoutActiveUsers = 1000,
            MaximumScheduledJobsStartShiftWithoutActiveUsers = 60000,
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        new GetInfoBasesResponse { InfoBases = new[] { infobase } }.Serialize(ref writer, 14);

        var reader = new RasReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        var decoded = GetInfoBasesResponse.Deserialize(ref reader, 14);

        var actual = Assert.Single(decoded.InfoBases);
        Assert.Equal(infobase.Uuid, actual.Uuid);
        Assert.Equal(infobase.DateOffset, actual.DateOffset);
        Assert.Equal(infobase.Dbms, actual.Dbms);
        Assert.Equal(infobase.DbName, actual.DbName);
        Assert.Equal(infobase.DbPwd, actual.DbPwd);
        Assert.Equal(infobase.DbServer, actual.DbServer);
        Assert.Equal(infobase.DbUser, actual.DbUser);
        Assert.Equal(infobase.DeniedFrom, actual.DeniedFrom);
        Assert.Equal(infobase.DeniedTo, actual.DeniedTo);
        Assert.Equal(infobase.DeniedMessage, actual.DeniedMessage);
        Assert.Equal(infobase.DeniedParameter, actual.DeniedParameter);
        Assert.Equal(infobase.Descr, actual.Descr);
        Assert.Equal(infobase.Locale, actual.Locale);
        Assert.Equal(infobase.Name, actual.Name);
        Assert.Equal(infobase.PermissionCode, actual.PermissionCode);
        Assert.Equal(infobase.ScheduledJobsDeny, actual.ScheduledJobsDeny);
        Assert.Equal(infobase.SecurityLevel, actual.SecurityLevel);
        Assert.Equal(infobase.SessionsDeny, actual.SessionsDeny);
        Assert.Equal(infobase.LicenseDistribution, actual.LicenseDistribution);
        Assert.Equal(infobase.ExternalSessionManagerConnectionString, actual.ExternalSessionManagerConnectionString);
        Assert.Equal(infobase.ExternalSessionManagerRequired, actual.ExternalSessionManagerRequired);
        Assert.Equal(infobase.SecurityProfileName, actual.SecurityProfileName);
        Assert.Equal(infobase.SafeModeSecurityProfileName, actual.SafeModeSecurityProfileName);
        Assert.True(actual.ReserveWorkingProcesses);
        Assert.True(actual.DisableLocalSpeechToText);
        Assert.Equal(42, actual.ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers);
        Assert.Equal(1000, actual.MinimumScheduledJobsStartPeriodWithoutActiveUsers);
        Assert.Equal(60000, actual.MaximumScheduledJobsStartShiftWithoutActiveUsers);
    }

    [Fact]
    public void GetInfoBasesResponse_Multiple_Items_Roundtrip_At_Version_14()
    {
        // Регресс на десинхронизацию: непрочитанный хвост v13/v14 сдвигал следующий элемент списка
        // («null expected» в ReadNullableSize на реальном RAS 8.3.25).
        var first = new InfobaseInfo
        {
            Uuid = Guid.Parse("7974161b-612e-4ace-859a-fc861d8b2823"),
            Name = "test_db1",
            Descr = "Первая база, описание на кириллице",
            Locale = "ru_RU",
            Dbms = "PostgreSQL",
            DisableLocalSpeechToText = false,
        };
        var second = new InfobaseInfo
        {
            Uuid = Guid.Parse("69a5437c-d49f-499b-9b1c-1a9bb1f7170e"),
            Name = "test_db3",
            Locale = "ru_RU",
            Dbms = "PostgreSQL",
            DisableLocalSpeechToText = true,
            ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers = 42,
            MinimumScheduledJobsStartPeriodWithoutActiveUsers = 1000,
            MaximumScheduledJobsStartShiftWithoutActiveUsers = 60000,
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        new GetInfoBasesResponse { InfoBases = new[] { first, second } }.Serialize(ref writer, 14);

        var reader = new RasReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        var decoded = GetInfoBasesResponse.Deserialize(ref reader, 14);

        Assert.Equal(2, decoded.InfoBases.Count);
        Assert.Equal(first.Uuid, decoded.InfoBases[0].Uuid);
        Assert.Equal(first.Descr, decoded.InfoBases[0].Descr);
        Assert.False(decoded.InfoBases[0].DisableLocalSpeechToText);
        Assert.Equal(second.Uuid, decoded.InfoBases[1].Uuid);
        Assert.True(decoded.InfoBases[1].DisableLocalSpeechToText);
        Assert.Equal(42, decoded.InfoBases[1].ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers);
        Assert.Equal(1000, decoded.InfoBases[1].MinimumScheduledJobsStartPeriodWithoutActiveUsers);
        Assert.Equal(60000, decoded.InfoBases[1].MaximumScheduledJobsStartShiftWithoutActiveUsers);
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(13, "01")]            // только disable_local_speech_to_text (стенд: v13 → 1 байт хвоста)
    [InlineData(14, "010000002A000003E80000EA60")] // bool + 3×int32 BE (замер RAS 8.3.25.1520, test_db3)
    public void InfobaseInfo_VersionTail_Has_Exact_Hex(int version, string expectedTailHex)
    {
        var info = new InfobaseInfo
        {
            Name = "ib",
            DisableLocalSpeechToText = true,
            ConfigurationUnloadDelayByWorkingProcessWithoutActiveUsers = 42,
            MinimumScheduledJobsStartPeriodWithoutActiveUsers = 1000,
            MaximumScheduledJobsStartShiftWithoutActiveUsers = 60000,
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        info.Serialize(ref writer, version);

        var bytes = buffer.WrittenSpan.ToArray();
        int tailLength = expectedTailHex.Length / 2;
        var actual = Convert.ToHexString(bytes[^tailLength..]);
        Assert.Equal(expectedTailHex, actual);
    }

    [Fact]
    public void GetInfoBasesResponse_ReserveWorkingProcesses_Requires_Version_9()
    {
        var infobase = new InfobaseInfo { Name = "erp", ReserveWorkingProcesses = true };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        new GetInfoBasesResponse { InfoBases = new[] { infobase } }.Serialize(ref writer, 8);

        var reader = new RasReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        var actual = Assert.Single(GetInfoBasesResponse.Deserialize(ref reader, 8).InfoBases);

        Assert.False(actual.ReserveWorkingProcesses);
        Assert.Equal("erp", actual.Name);
    }
}

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

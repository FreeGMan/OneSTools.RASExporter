using System;
using System.Buffers;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Messages;

public sealed class AuthAndAdminMessagesTests
{
    [Fact]
    public void AuthenticateRequest_Serializes_Correctly()
    {
        var clusterId = Guid.NewGuid();
        var req = new AuthenticateRequest(clusterId, "admin", "secret123");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        req.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);

        Assert.Equal(clusterId, reader.ReadUuid());
        Assert.Equal("admin", reader.ReadString());
        Assert.Equal("secret123", reader.ReadString());
    }

    [Fact]
    public void GetAgentVersionResponse_Roundtrips()
    {
        var resp = new GetAgentVersionResponse { Version = "8.3.24.1342" };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        resp.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetAgentVersionResponse.Deserialize(ref reader);

        Assert.Equal("8.3.24.1342", deserialized.Version);
    }

    [Fact]
    public void GetAgentAdminsResponse_Roundtrips()
    {
        var admin = new AdminInfo
        {
            Name = "ClusterAdministrator",
            Description = "Main cluster admin account",
            Password = "",
            PasswordAuthAllowed = true,
            SysAuthAllowed = true,
            SysUserName = "DOMAIN\\Admin"
        };

        var resp = new GetAgentAdminsResponse { Admins = new[] { admin } };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        resp.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetAgentAdminsResponse.Deserialize(ref reader);

        Assert.Single(deserialized.Admins);
        var a = deserialized.Admins[0];
        Assert.Equal("ClusterAdministrator", a.Name);
        Assert.True(a.PasswordAuthAllowed);
        Assert.Equal("DOMAIN\\Admin", a.SysUserName);
    }
}

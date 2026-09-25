using System;
using System.Buffers;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Wire;

public sealed class NestedListBoundsTests
{
    [Fact]
    public void Session_Licenses_Reject_Huge_Count() => AssertBounded(
        (ref RasWriter writer) => new SessionInfo().Serialize(ref writer),
        (ref RasWriter writer) => new SessionInfo { Licenses = [new LicenseInfo()] }.Serialize(ref writer),
        (ref RasReader reader) => SessionInfo.Deserialize(ref reader));

    [Fact]
    public void Process_Licenses_Reject_Huge_Count() => AssertBounded(
        (ref RasWriter writer) => new ProcessInfo().Serialize(ref writer),
        (ref RasWriter writer) => new ProcessInfo { Licenses = [new LicenseInfo()] }.Serialize(ref writer),
        (ref RasReader reader) => ProcessInfo.Deserialize(ref reader));

    [Fact]
    public void Server_Port_Ranges_Reject_Huge_Count() => AssertBounded(
        (ref RasWriter writer) => new ServerInfo().Serialize(ref writer),
        (ref RasWriter writer) => new ServerInfo { PortRanges = [new PortRange(0, 0)] }.Serialize(ref writer),
        (ref RasReader reader) => ServerInfo.Deserialize(ref reader));

    [Fact]
    public void Service_Managers_Reject_Huge_Count() => AssertBounded(
        (ref RasWriter writer) => new ServiceInfo().Serialize(ref writer),
        (ref RasWriter writer) => new ServiceInfo { Managers = [Guid.Empty] }.Serialize(ref writer),
        (ref RasReader reader) => ServiceInfo.Deserialize(ref reader));

    private delegate void WriteModel(ref RasWriter writer);
    private delegate object ReadModel(ref RasReader reader);

    private static void AssertBounded(WriteModel empty, WriteModel nonEmpty, ReadModel deserialize)
    {
        var original = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(original);
        empty(ref writer);

        var withItem = new ArrayBufferWriter<byte>();
        writer = new RasWriter(withItem);
        nonEmpty(ref writer);

        var originalBytes = original.WrittenSpan.ToArray();
        var changed = withItem.WrittenSpan;
        int countOffset = 0;
        while (originalBytes[countOffset] == changed[countOffset]) countOffset++;
        Assert.Equal((byte)0, originalBytes[countOffset]);
        Assert.Equal((byte)1, changed[countOffset]);

        var attack = new ArrayBufferWriter<byte>();
        writer = new RasWriter(attack);
        writer.WriteBytes(originalBytes.AsSpan(0, countOffset));
        writer.WriteSize(RasWireConstants.MaxListSize + 1);
        writer.WriteBytes(originalBytes.AsSpan(countOffset + 1));

        Assert.Throws<RasProtocolException>(() =>
        {
            var r = new RasReader(new ReadOnlySequence<byte>(attack.WrittenMemory));
            deserialize(ref r);
        });
    }
}

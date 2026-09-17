using System;
using System.Buffers;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Wire;

public sealed class RasReaderTests
{
    public delegate T ReaderFunc<T>(ref RasReader reader);

    private T Read<T>(byte[] data, ReaderFunc<T> readFunc)
    {
        var sequence = new ReadOnlySequence<byte>(data);
        var reader = new RasReader(sequence);
        return readFunc(ref reader);
    }

    [Fact]
    public void ReadByte_Value()
    {
        Assert.Equal(0x7F, Read(new byte[] { 0x7F }, (ref RasReader r) => r.ReadByte()));
        Assert.Equal(0x80, Read(new byte[] { 0x80 }, (ref RasReader r) => r.ReadByte()));
    }

    [Fact]
    public void ReadShort_Value()
    {
        Assert.Equal(0x1234, Read(new byte[] { 0x12, 0x34 }, (ref RasReader r) => r.ReadShort()));
        Assert.Equal(256, Read(new byte[] { 0x01, 0x00 }, (ref RasReader r) => r.ReadShort()));
    }

    [Fact]
    public void ReadInt32_Value()
    {
        Assert.Equal(0x12345678, Read(new byte[] { 0x12, 0x34, 0x56, 0x78 }, (ref RasReader r) => r.ReadInt32()));
        Assert.Equal(475223888, Read(new byte[] { 0x1C, 0x53, 0x57, 0x50 }, (ref RasReader r) => r.ReadInt32()));
    }

    [Fact]
    public void ReadLong_Value()
    {
        Assert.Equal(0x0102030405060708, Read(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, (ref RasReader r) => r.ReadLong()));
    }

    [Fact]
    public void ReadSize_Value()
    {
        Assert.Equal(0, Read(new byte[] { 0x00 }, (ref RasReader r) => r.ReadSize()));
        Assert.Equal(127, Read(new byte[] { 0x7F }, (ref RasReader r) => r.ReadSize()));
        Assert.Equal(300, Read(new byte[] { 0xAC, 0x02 }, (ref RasReader r) => r.ReadSize()));
        Assert.Equal(16384, Read(new byte[] { 0x80, 0x80, 0x01 }, (ref RasReader r) => r.ReadSize()));
    }

    [Fact]
    public void ReadNullableSize_Value()
    {
        Assert.Null(Read(new byte[] { 0x80 }, (ref RasReader r) => r.ReadNullableSize()));
        Assert.Equal(0, Read(new byte[] { 0x00 }, (ref RasReader r) => r.ReadNullableSize()));
        Assert.Equal(300, Read(new byte[] { 0x6C, 0x04 }, (ref RasReader r) => r.ReadNullableSize()));
    }

    [Fact]
    public void ReadString_Value()
    {
        Assert.Equal("hello", Read(new byte[] { 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, (ref RasReader r) => r.ReadString()));
        Assert.Equal("", Read(new byte[] { 0x00 }, (ref RasReader r) => r.ReadString()));
        Assert.Null(Read(new byte[] { 0x80 }, (ref RasReader r) => r.ReadString()));
    }

    [Fact]
    public void ReadUuid_Value()
    {
        var expected = Guid.Parse("123e4567-e89b-12d3-a456-426614174000");
        Assert.Equal(
            expected,
            Read(new byte[] { 0x12, 0x3E, 0x45, 0x67, 0xE8, 0x9B, 0x12, 0xD3, 0xA4, 0x56, 0x42, 0x66, 0x14, 0x17, 0x40, 0x00 }, (ref RasReader r) => r.ReadUuid())
        );
    }

    [Fact]
    public void ReadFloat_Value()
    {
        Assert.Equal(1.0f, Read(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, (ref RasReader r) => r.ReadFloat()));
        Assert.Equal(-2.5f, Read(new byte[] { 0xC0, 0x20, 0x00, 0x00 }, (ref RasReader r) => r.ReadFloat()));
    }

    [Fact]
    public void ReadDouble_Value()
    {
        Assert.Equal(1.0, Read(new byte[] { 0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, (ref RasReader r) => r.ReadDouble()));
    }

    [Fact]
    public void ReadBool_Value()
    {
        Assert.True(Read(new byte[] { 0x01 }, (ref RasReader r) => r.ReadBool()));
        Assert.False(Read(new byte[] { 0x00 }, (ref RasReader r) => r.ReadBool()));
    }

    [Fact]
    public void ReadType_Value()
    {
        Assert.Equal(ServiceWireType.Int, Read(new byte[] { 0x04 }, (ref RasReader r) => r.ReadType()));
        Assert.Null(Read(new byte[] { 0x80 }, (ref RasReader r) => r.ReadType()));
    }

    [Fact]
    public void ReadTime_Value()
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(epoch, Read(new byte[] { 0x00, 0x02, 0x35, 0x1E, 0xB5, 0xC0, 0x70, 0x00 }, (ref RasReader r) => r.ReadTime()));

        var date2021 = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(date2021, Read(new byte[] { 0x00, 0x02, 0x43, 0xC2, 0x06, 0x30, 0xD0, 0x00 }, (ref RasReader r) => r.ReadTime()));
    }
}
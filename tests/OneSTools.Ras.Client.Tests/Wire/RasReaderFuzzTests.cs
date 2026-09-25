using System;
using System.Buffers;
using System.IO;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Wire;

/// <summary>
/// Fuzz-регрессии харденинга парсера (TODO.md T7/S1): повреждённые/враждебные длины и
/// varint не должны приводить к переполнению int, гипертрефовым аллокациям или безграничной
/// рекурсии — только к RasProtocolException/EndOfStreamException.
/// </summary>
public sealed class RasReaderFuzzTests
{
    private static RasReader Reader(params byte[] data) => new(new ReadOnlySequence<byte>(data));

    [Fact]
    public void ReadSize_OverlongVarInt_ThrowsProtocol()
    {
        // 0x80 повторяется дольше допустимых 5 байт
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F };
        Assert.Throws<RasProtocolException>(() => Reader(data).ReadSize());
    }

    [Fact]
    public void ReadSize_OverflowingAccumulation_ThrowsProtocol()
    {
        // 5 групп по 7 бит со значениями, дающими переполнение int32
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Assert.Throws<RasProtocolException>(() => Reader(data).ReadSize());
    }

    [Fact]
    public void ReadSize_NegativeAfterShift_ThrowsProtocol()
    {
        // последняя группа сдвигает в знаковый бит
        var data = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 };
        Assert.Throws<RasProtocolException>(() => Reader(data).ReadSize());
    }

    [Fact]
    public void ReadSize_Fifth_Byte_Cannot_Wrap_Around()
        => Assert.Throws<RasProtocolException>(() => Reader(0x80, 0x80, 0x80, 0x80, 0x10).ReadSize());

    [Fact]
    public void ReadNullableSize_Fifth_Byte_Cannot_Wrap_Around()
        => Assert.Throws<RasProtocolException>(() => Reader(0x40, 0x80, 0x80, 0x80, 0x10).ReadNullableSize());

    [Theory]
    [InlineData(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })] // overlong continuation
    [InlineData(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0xFF })]        // overflow accumulation
    public void ReadNullableSize_MaliciousVarInt_ThrowsProtocol(byte[] data)
    {
        Assert.Throws<RasProtocolException>(() => Reader(data).ReadNullableSize());
    }

    [Fact]
    public void ReadBytes_HugeLength_ThrowsProtocol_NoAllocation()
    {
        // length > MaxBytesLength
        Assert.Throws<RasProtocolException>(() => Reader(new byte[64]).ReadBytes(RasWireConstants.MaxBytesLength + 1));
    }

    [Fact]
    public void ReadBytes_NegativeLength_ThrowsProtocol()
        => Assert.Throws<RasProtocolException>(() => Reader(Array.Empty<byte>()).ReadBytes(-1));

    [Fact]
    public void ReadBytes_LengthBeyondRemaining_ThrowsEndOfStream()
    {
        var data = new byte[] { 1, 2, 3 };
        Assert.Throws<EndOfStreamException>(() => Reader(data).ReadBytes(1000));
    }

    [Fact]
    public void ReadString_HugeLength_ThrowsProtocol()
    {
        // overlong nullable_size ⇒ протокольная ошибка до любой аллокации
        var data = new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Assert.Throws<RasProtocolException>(() => Reader(data).ReadString());
    }

    [Fact]
    public void ReadListCount_BeyondRemaining_ThrowsEndOfStream()
    {
        // size=50 при 2 доступных байтах (каждый элемент ≥1 байта)
        var data = new byte[] { 50, 0x01, 0x02 };
        Assert.Throws<EndOfStreamException>(() => Reader(data).ReadListCount());
    }

    [Fact]
    public void ReadListCount_AboveMax_ThrowsProtocol()
    {
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }; // огромный size
        Assert.Throws<RasProtocolException>(() => Reader(data).ReadListCount());
    }

    [Fact]
    public void ReadUuid_Truncated_ThrowsEndOfStream()
    {
        var data = new byte[10];
        Assert.Throws<EndOfStreamException>(() => Reader(data).ReadUuid());
    }

    [Fact]
    public void ReadTime_Truncated_ThrowsEndOfStream()
        => Assert.Throws<EndOfStreamException>(() => Reader(new byte[4]).ReadTime());

    [Fact]
    public void ReadBool_InvalidByte_ThrowsInvalidData()
        => Assert.Throws<InvalidDataException>(() => Reader(new byte[] { 7 }).ReadBool());

    [Fact]
    public void ReadType_UnknownCode_ThrowsInvalidData()
        => Assert.Throws<InvalidDataException>(() => Reader(new byte[] { 0x7F }).ReadType());

    [Fact]
    public void ReadThrowable_DeepCauseChain_ThrowsProtocol()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new RasWriter(buf);
        Exception? ex = null;
        for (int i = 0; i < 40; i++)
            ex = new RasRemoteException($"cls{i}", "msg", Array.Empty<RasRemoteStackFrame>(), ex);
        RasThrowableSerializer.WriteThrowable(ref w, ex);

        var seq = new ReadOnlySequence<byte>(buf.WrittenMemory.ToArray());
        Assert.Throws<RasProtocolException>(() =>
        {
            var r = new RasReader(seq);
            _ = RasThrowableSerializer.ReadThrowable(ref r);
        });
    }

    [Theory]
    [InlineData(new byte[] { 0x82 })]                                        // null-marker с мусором в payload
    [InlineData(new byte[] { 0x7F, 0x80, 0x80, 0x80, 0x80, 0x80 })]          // overlong continuation
    public void ReadNullableSize_Garbage_ThrowsDeterminedly(byte[] data)
    {
        Assert.ThrowsAny<Exception>(() => Reader(data).ReadNullableSize());
    }
}

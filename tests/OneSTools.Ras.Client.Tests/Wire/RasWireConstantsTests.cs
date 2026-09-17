using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Tests.Wire;

public sealed class RasWireConstantsTests
{
    [Fact]
    public void NullByte_Is_0x80()
    {
        Assert.Equal(0x80, RasWireConstants.NullByte);
    }

    [Fact]
    public void NextMask_Matches_LsbMask_Plus_Continuation()
    {
        Assert.Equal(0x80, RasWireConstants.NextMask);
        Assert.Equal(0x7F, RasWireConstants.LsbMask);
    }

    [Fact]
    public void NullNextMask_Is_0x40_With_6_Bit_Payload()
    {
        Assert.Equal(0x40, RasWireConstants.NullNextMask);
        Assert.Equal(0x3F, RasWireConstants.NullLsbMask);
    }

[Fact]
    public void AgeDelta_Is_Hundred_Microsecond_Units_Between_0001_01_01_And_Unix_Epoch()
    {
        var unixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // AgeDelta задан в единицах по 100 мкс (т.е. 1000 "стандартных" 100нс-тиков .NET).
        // См. docs/CODEC_SPEC.md, комментарий к AgeDelta.
        long expectedIn100UsUnits = unixEpoch.Ticks / 1000;

        Assert.Equal(RasWireConstants.AgeDelta, expectedIn100UsUnits);
    }
}
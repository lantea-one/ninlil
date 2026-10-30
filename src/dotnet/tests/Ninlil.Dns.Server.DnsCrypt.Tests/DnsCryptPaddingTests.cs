// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="DnsCryptPadding" /> on byte arrays alone: the ISO/IEC 7816-4
///     layout, the two client query paddings of DNSCrypt v2 sections 3 and 5, and the round trip
///     through <see cref="DnsCryptPadding.Unpad" />.
/// </summary>
public class DnsCryptPaddingTests
{
    [Theory]
    [InlineData(0, 64)]
    [InlineData(35, 64)]
    [InlineData(56, 64)]
    [InlineData(63, 64)]
    [InlineData(64, 128)]
    public void PadToBlockSize_AddsOneToSixtyFourBytesAndEndsOnABlock(int length, int expected)
    {
        byte[] padded = DnsCryptPadding.PadToBlockSize(new byte[length], 64);

        Assert.Equal(expected, padded.Length);
        Assert.Equal(0x80, padded[length]);
    }

    [Theory]
    [InlineData(35, 256)]
    [InlineData(200, 256)]
    [InlineData(255, 256)]
    [InlineData(256, 320)]
    [InlineData(300, 320)]
    public void PadToMinimumLength_PadsAtLeastToTheMinimumOnABlock(int length, int expected)
    {
        byte[] padded = DnsCryptPadding.PadToMinimumLength(new byte[length], 64, 256);

        Assert.Equal(expected, padded.Length);
        Assert.Equal(0x80, padded[length]);
        Assert.Equal(new byte[length], DnsCryptPadding.Unpad(padded));
    }

    [Theory]
    [InlineData(56, 0, 8)]
    [InlineData(56, 1, 72)]
    [InlineData(56, 2, 136)]
    [InlineData(56, 3, 200)]
    public void PadWithExtraBlocks_GivesTheSpecificationsExamplePads(
        int length,
        int extraBlocks,
        int expectedPadLength
    )
    {
        // DNSCrypt v2 section 5: a 56-byte query can carry a pad of 8, 72, 136 or 200 bytes.
        byte[] padded = DnsCryptPadding.PadWithExtraBlocks(new byte[length], 64, extraBlocks);

        Assert.Equal(length + expectedPadLength, padded.Length);
        Assert.Equal(0, padded.Length % 64);
        Assert.Equal(new byte[length], DnsCryptPadding.Unpad(padded));
    }
}

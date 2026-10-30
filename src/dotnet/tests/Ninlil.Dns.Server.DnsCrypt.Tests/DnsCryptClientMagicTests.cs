// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="DnsCryptClientMagic" /> with scripted candidates, because the
///     two rules of DNSCrypt v2 section 12 (no seven leading zero bytes, no sharing between valid
///     certificates) are met by chance in practice, so a random draw never exercises them.
/// </summary>
public class DnsCryptClientMagicTests
{
    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }, false)]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 1, 0 }, true)]
    [InlineData(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0 }, false)]
    public void IsAllowed_RejectsSevenLeadingZeroBytesAndAWrongLength(byte[] magic, bool expected)
    {
        Assert.Equal(expected, DnsCryptClientMagic.IsAllowed(magic));
    }

    [Fact]
    public void Generate_SkipsCandidatesWithSevenLeadingZeroBytesAndCandidatesInUse()
    {
        byte[] inUse = [1, 2, 3, 4, 5, 6, 7, 8];
        Queue<byte[]> candidates = new([
            [0, 0, 0, 0, 0, 0, 0, 5],
            inUse,
            [9, 9, 9, 9, 9, 9, 9, 9],
        ]);

        byte[] magic = DnsCryptClientMagic.Generate([inUse], () => candidates.Dequeue());

        Assert.Equal(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }, magic);
        Assert.Empty(candidates);
    }

    [Fact]
    public void Generate_WithTheRandomSource_ReturnsAnAllowedEightByteTag()
    {
        byte[] magic = DnsCryptClientMagic.Generate([]);

        Assert.Equal(8, magic.Length);
        Assert.True(DnsCryptClientMagic.IsAllowed(magic));
    }
}

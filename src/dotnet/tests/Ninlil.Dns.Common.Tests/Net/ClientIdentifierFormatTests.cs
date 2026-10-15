using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Net;

/// <summary>
///     This class tests <see cref="ClientIdentifierFormat" /> with plain strings: every accepted
///     spelling reduces to the one normal form, malformed text and over-long identifiers are
///     refused, and equality is decided on the normal form.
/// </summary>
public sealed class ClientIdentifierFormatTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "aa:bb:cc:dd:ee:ff")]
    [InlineData("aa-bb-cc-dd-ee-ff", "aa:bb:cc:dd:ee:ff")]
    [InlineData("aabb.ccdd.eeff", "aa:bb:cc:dd:ee:ff")]
    [InlineData("AABBCCDDEEFF", "aa:bb:cc:dd:ee:ff")]
    [InlineData("0:1b:63:84:45:e6", "00:1b:63:84:45:e6")]
    [InlineData(" 01:aa:bb:cc:dd:ee:ff ", "01:aa:bb:cc:dd:ee:ff")]
    [InlineData(
        "00:01:00:01:2C:8B:9C:3D:AA:BB:CC:DD:EE:01",
        "00:01:00:01:2c:8b:9c:3d:aa:bb:cc:dd:ee:01"
    )]
    [InlineData("aa", "aa")]
    public void TryNormalize_ForAnAcceptedSpelling_ReturnsTheNormalForm(
        string input,
        string expected
    )
    {
        Assert.True(ClientIdentifierFormat.TryNormalize(input, out string normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aa:bb-cc")]
    [InlineData("aabbc")]
    [InlineData("0xaabb")]
    [InlineData("aa::bb")]
    [InlineData("aabb.ccd")]
    [InlineData("declined:10.0.0.5")]
    [InlineData("aa:bb:cc:")]
    [InlineData("aaa:bb")]
    [InlineData("zz:bb")]
    public void TryNormalize_ForARefusedInput_ReturnsFalse(string input)
    {
        Assert.False(ClientIdentifierFormat.TryNormalize(input, out string normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_At85Bytes_IsAcceptedAnd86BytesAreRefused()
    {
        string eightyFive = string.Join(':', Enumerable.Repeat("ab", 85));
        string eightySix = string.Join(':', Enumerable.Repeat("ab", 86));

        Assert.True(ClientIdentifierFormat.TryNormalize(eightyFive, out string normalized));
        Assert.Equal(254, normalized.Length);
        Assert.False(ClientIdentifierFormat.TryNormalize(eightySix, out _));
    }

    [Fact]
    public void AreEqual_ForTwoSpellingsOfOneIdentifier_IsTrue() =>
        Assert.True(ClientIdentifierFormat.AreEqual("AA-BB", "aa:bb"));

    [Fact]
    public void AreEqual_ForADeclineKeyAndAnIdentifierWithTheSameDigits_IsFalse() =>
        Assert.False(ClientIdentifierFormat.AreEqual("declined:10.0.0.5", "de:ce:d1:00:05"));

    [Fact]
    public void Format_WritesLowercaseColonSeparatedHex() =>
        Assert.Equal("00:1b:ff", ClientIdentifierFormat.Format([0x00, 0x1b, 0xff]));
}

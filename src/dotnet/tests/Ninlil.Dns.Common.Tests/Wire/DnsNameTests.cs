using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnsName" />: the canonical lower-case form of RFC 4034 §6.1 and
///     §6.2, case-insensitive equality, and uncompressed wire encoding. The tests are in-memory
///     checks.
/// </summary>
public class DnsNameTests
{
    [Fact]
    public void ToCanonicalLowerLowersEveryAsciiLetter()
    {
        DnsName mixedCase = DnsName.Parse("WWW.Example.COM");

        DnsName lowered = mixedCase.ToCanonicalLower();

        Assert.Equal("www.example.com.", lowered.ToString());
    }

    [Fact]
    public void ToCanonicalLowerReturnsSameInstanceWhenAlreadyLowercase()
    {
        DnsName alreadyLower = DnsName.Parse("www.example.com");

        DnsName result = alreadyLower.ToCanonicalLower();

        Assert.Same(alreadyLower, result);
    }

    [Fact]
    public void NamesCompareCaseInsensitively()
    {
        Assert.Equal(DnsName.Parse("WWW.example.com"), DnsName.Parse("www.EXAMPLE.com"));
    }

    [Fact]
    public void ToUncompressedWireBytesRoundTripsThroughParse()
    {
        DnsName name = DnsName.Parse("www.example.com");

        byte[] wireBytes = name.ToUncompressedWireBytes();

        // 3("www") + 1 + 7("example") + 1 + 3("com") + 1 + 1(root) = 4+8+4+1 = 17.
        Assert.Equal(17, wireBytes.Length);
        Assert.Equal(0, wireBytes[^1]); // Terminating root label.
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData(".example")]
    [InlineData("..")]
    [InlineData("bücher.example")]
    [InlineData("\\ü")]
    [InlineData("Ā")]
    public void TryParseRejectsEmptyLabelsAndNonAsciiCharacters(string presentation)
    {
        Assert.False(DnsName.TryParse(presentation, out _));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("example.")]
    [InlineData("a\\.b.example")]
    [InlineData("\\195\\188.example")]
    public void TryParseAcceptsRootTrailingDotEscapedDotAndExplicitBytes(string presentation)
    {
        Assert.True(DnsName.TryParse(presentation, out _));
    }

    [Fact]
    public void ExplicitDecimalEscapesStoreTheRawBytes()
    {
        DnsName name = DnsName.Parse("\\195\\188.example");

        Assert.Equal(new byte[] { 195, 188 }, name.Labels[0]);
    }

    [Fact]
    public void TheConstructorRejectsAnEmptyLabel()
    {
        Assert.Throws<ArgumentException>(() =>
            new DnsName([
                [],
            ])
        );
    }
}

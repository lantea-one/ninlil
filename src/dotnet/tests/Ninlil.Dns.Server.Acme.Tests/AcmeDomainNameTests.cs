using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Acme;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="AcmeDomainName.TryParse" />, the host-name rules for an ACME
///     certificate domain, with plain string inputs and no host.
/// </summary>
public sealed class AcmeDomainNameTests
{
    [Theory]
    [InlineData("www.example.com", "www.example.com", "www.example.com")]
    [InlineData("Example.COM.", "example.com", "example.com")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("*.example.com", "*.example.com", "example.com")]
    public void TryParse_ForAValidName_NormalizesItAndFindsTheChallengeBase(
        string domain,
        string expectedNormalized,
        string expectedBase
    )
    {
        bool valid = AcmeDomainName.TryParse(
            domain,
            out string normalized,
            out DnsName challengeBase,
            out string error
        );

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(expectedNormalized, normalized);
        Assert.Equal(expectedBase, challengeBase.ToString().TrimEnd('.'));
    }

    [Theory]
    [InlineData("exa mple.com")]
    [InlineData("_acme.example.com")]
    [InlineData("-a.example.com")]
    [InlineData("a-.example.com")]
    [InlineData("a..example.com")]
    [InlineData("bücher.example")]
    [InlineData("*.*.example.com")]
    [InlineData("www.*.example.com")]
    [InlineData("com")]
    [InlineData("*.com")]
    [InlineData("")]
    [InlineData("  ")]
    public void TryParse_ForAnInvalidName_ReturnsFalseWithAnError(string domain)
    {
        bool valid = AcmeDomainName.TryParse(
            domain,
            out _,
            out DnsName challengeBase,
            out string error
        );

        Assert.False(valid);
        Assert.Null(challengeBase);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryParse_ForALabelOf64Characters_ReturnsFalse() =>
        Assert.False(
            AcmeDomainName.TryParse($"{new string('a', 64)}.example.com", out _, out _, out _)
        );

    [Fact]
    public void TryParse_ForALabelOf63Characters_ReturnsTrue() =>
        Assert.True(
            AcmeDomainName.TryParse($"{new string('a', 63)}.example.com", out _, out _, out _)
        );

    [Fact]
    public void TryParse_ForANameOf254Characters_ReturnsFalse()
    {
        // Three 63-character labels and a shorter last label make exactly 254 characters.
        string name = string.Join('.', Enumerable.Repeat(new string('a', 63), 3)) + ".";
        name += new string('b', 254 - name.Length);

        Assert.Equal(254, name.Length);
        Assert.False(AcmeDomainName.TryParse(name, out _, out _, out _));
    }

    [Fact]
    public void TryParse_ForANameOf253Characters_ReturnsTrue()
    {
        string name = string.Join('.', Enumerable.Repeat(new string('a', 63), 3)) + ".";
        name += new string('b', 253 - name.Length);

        Assert.Equal(253, name.Length);
        Assert.True(AcmeDomainName.TryParse(name, out _, out _, out _));
    }
}

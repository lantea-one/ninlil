using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnsName" />, including its RFC 4034 §6.1/§6.2 canonical-form
///     support.
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
}

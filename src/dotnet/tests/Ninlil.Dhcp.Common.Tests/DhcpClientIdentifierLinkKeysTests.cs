using Ninlil.Dhcp.Common.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This class covers <see cref="DhcpClientIdentifierLinkKeys" />: every DHCPv4 identifier
///     shape and every DUID type it recognizes, the cross-protocol matches a dual-stack device
///     produces, and the shapes that must yield nothing rather than a guess.
/// </summary>
public class DhcpClientIdentifierLinkKeysTests
{
    /// <summary>A sample Ethernet MAC address used throughout.</summary>
    private const string Mac = "aa:bb:cc:dd:ee:01";

    /// <summary>
    ///     This test proves a plain chaddr identifier yields its MAC.
    /// </summary>
    [Fact]
    public void APlainChaddrYieldsItsMac() =>
        Assert.Equal(["mac:aabbccddee01"], DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(Mac));

    /// <summary>
    ///     This test proves an RFC 2132 hardware-type-prefixed Client-identifier yields its MAC.
    /// </summary>
    [Fact]
    public void AnRfc2132EthernetClientIdentifierYieldsItsMac() =>
        Assert.Equal(
            ["mac:aabbccddee01"],
            DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier("01:" + Mac)
        );

    /// <summary>
    ///     This test proves a DUID-LLT with an Ethernet address yields both its DUID and its MAC.
    /// </summary>
    [Fact]
    public void ADuidLltYieldsItsDuidAndMac()
    {
        // type 1, hardware type 1, a 4-octet time, then the MAC.
        const string duidLlt = "00:01:00:01:2a:bc:de:f0:" + Mac;

        IReadOnlySet<string> keys = DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(duidLlt);

        Assert.Contains("mac:aabbccddee01", keys);
        Assert.Contains("duid:000100012abcdef0aabbccddee01", keys);
    }

    /// <summary>
    ///     This test proves a DUID-LL with an Ethernet address yields its MAC.
    /// </summary>
    [Fact]
    public void ADuidLlYieldsItsMac() =>
        Assert.Contains(
            "mac:aabbccddee01",
            DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier("00:03:00:01:" + Mac)
        );

    /// <summary>
    ///     This test proves a DUID-UUID yields only its DUID key, since it embeds no MAC.
    /// </summary>
    [Fact]
    public void ADuidUuidYieldsOnlyItsDuid()
    {
        const string duidUuid = "00:04:11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff:00";

        IReadOnlySet<string> keys = DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(duidUuid);

        Assert.Equal(["duid:0004112233445566778899aabbccddeeff00"], keys);
    }

    /// <summary>
    ///     This test proves a DUID-LLT whose hardware type is not Ethernet yields no MAC.
    /// </summary>
    [Fact]
    public void ANonEthernetDuidLltYieldsNoMac() =>
        Assert.DoesNotContain(
            DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier("00:01:00:06:2a:bc:de:f0:" + Mac),
            key => key.StartsWith("mac:", StringComparison.Ordinal)
        );

    /// <summary>
    ///     This test proves an RFC 4361 DHCPv4 identifier shares its DUID key with the same
    ///     client's DHCPv6 identifier, even for a DUID that embeds no MAC.
    /// </summary>
    [Fact]
    public void AnRfc4361ClientIdentifierLinksToTheSameDuidOverDhcpv6()
    {
        const string duidEn = "00:02:00:00:ab:11:de:ad:be:ef";
        string rfc4361 = "ff:12:34:56:78:" + duidEn;

        IReadOnlySet<string> v4 = DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(rfc4361);
        IReadOnlySet<string> v6 = DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(duidEn);

        Assert.True(v4.Overlaps(v6));
    }

    /// <summary>
    ///     This test proves a dual-stack device's chaddr and its DUID-LLT share a key.
    /// </summary>
    [Fact]
    public void AChaddrAndADuidLltForTheSameNicShareAKey() =>
        Assert.True(
            DhcpClientIdentifierLinkKeys
                .FromDhcpv4Identifier(Mac)
                .Overlaps(
                    DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(
                        "00:01:00:01:2a:bc:de:f0:" + Mac
                    )
                )
        );

    /// <summary>
    ///     This test proves free-form, malformed, or empty identifiers yield nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a mac")]
    [InlineData("living-room-tv")]
    [InlineData("aa:bb:cc:dd:ee:0")]
    [InlineData("00:01")]
    public void AnUninterpretableIdentifierYieldsNoKeys(string identifier)
    {
        Assert.Empty(DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(identifier));
        Assert.Empty(DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(identifier));
    }
}

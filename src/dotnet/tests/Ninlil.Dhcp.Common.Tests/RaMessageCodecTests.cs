using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class checks that <see cref="RaMessageWriter" /> and <see cref="RaMessageReader" />
///     round-trip a <see cref="RouterSolicitationMessage" /> and a
///     <see cref="RouterAdvertisementMessage" /> with their options, in memory with no socket, and
///     that malformed input is rejected or the bad option skipped. It is the Router Advertisement
///     counterpart of <see cref="Dhcpv6MessageCodecTests" />. The raw ICMPv6 socket needs
///     <c>CAP_NET_RAW</c>; <c>Icmpv6RaListenerLiveServiceControlTests</c> exercises it when that
///     is available (see <c>Ninlil.Dhcp.Protocol.Ra.Icmpv6RaListener</c>).
/// </summary>
public class RaMessageCodecTests
{
    [Fact]
    public void RouterSolicitation_RoundTrips_WithSourceLinkLayerAddress()
    {
        RouterSolicitationMessage solicitation = new();
        solicitation.SetSourceLinkLayerAddress([0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff]);

        byte[] bytes = RaMessageWriter.WriteRouterSolicitation(solicitation);

        Assert.True(
            RaMessageReader.TryReadRouterSolicitation(
                bytes,
                out RouterSolicitationMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(decoded.TryGetSourceLinkLayerAddress(out byte[] linkLayerAddress));
        Assert.Equal(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }, linkLayerAddress);
    }

    [Fact]
    public void RouterSolicitation_RoundTrips_WithNoOptionsAtAll()
    {
        RouterSolicitationMessage solicitation = new();

        byte[] bytes = RaMessageWriter.WriteRouterSolicitation(solicitation);

        Assert.True(
            RaMessageReader.TryReadRouterSolicitation(
                bytes,
                out RouterSolicitationMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.Empty(decoded.Options);
        Assert.False(decoded.TryGetSourceLinkLayerAddress(out _));
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_EveryFixedFieldAndFlag()
    {
        RouterAdvertisementMessage advertisement = new()
        {
            CurrentHopLimit = 64,
            ManagedAddressConfiguration = true,
            OtherConfiguration = true,
            RouterLifetime = TimeSpan.FromSeconds(1800),
            ReachableTime = TimeSpan.FromSeconds(30),
            RetransTimer = TimeSpan.FromSeconds(1),
        };

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.Equal(64, decoded.CurrentHopLimit);
        Assert.True(decoded.ManagedAddressConfiguration);
        Assert.True(decoded.OtherConfiguration);
        Assert.Equal(TimeSpan.FromSeconds(1800), decoded.RouterLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), decoded.ReachableTime);
        Assert.Equal(TimeSpan.FromSeconds(1), decoded.RetransTimer);
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_NotADefaultRouter_WithBothFlagsClear()
    {
        RouterAdvertisementMessage advertisement = new() { RouterLifetime = TimeSpan.Zero };

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.Equal(TimeSpan.Zero, decoded.RouterLifetime);
        Assert.False(decoded.ManagedAddressConfiguration);
        Assert.False(decoded.OtherConfiguration);
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_MultiplePrefixInformationOptions()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.AddPrefixInformation(
            new RaPrefixInformationOption
            {
                PrefixLength = 64,
                OnLink = true,
                Autonomous = true,
                ValidLifetime = RaPrefixInformationOption.InfiniteLifetime,
                PreferredLifetime = TimeSpan.FromHours(4),
                Prefix = IPAddress.Parse("2001:db8:1::"),
            }
        );
        advertisement.AddPrefixInformation(
            new RaPrefixInformationOption
            {
                PrefixLength = 60,
                OnLink = false,
                Autonomous = false,
                ValidLifetime = TimeSpan.FromDays(1),
                PreferredLifetime = TimeSpan.FromHours(12),
                Prefix = IPAddress.Parse("2001:db8:2000::"),
            }
        );

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(
            decoded.TryGetPrefixInformationOptions(
                out IReadOnlyList<RaPrefixInformationOption> prefixes
            )
        );
        Assert.Equal(2, prefixes.Count);

        Assert.Equal(64, prefixes[0].PrefixLength);
        Assert.True(prefixes[0].OnLink);
        Assert.True(prefixes[0].Autonomous);
        Assert.Equal(RaPrefixInformationOption.InfiniteLifetime, prefixes[0].ValidLifetime);
        Assert.Equal(IPAddress.Parse("2001:db8:1::"), prefixes[0].Prefix);

        Assert.Equal(60, prefixes[1].PrefixLength);
        Assert.False(prefixes[1].OnLink);
        Assert.False(prefixes[1].Autonomous);
        Assert.Equal(IPAddress.Parse("2001:db8:2000::"), prefixes[1].Prefix);
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_MtuAndRecursiveDnsServerOptions()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetMtu(1500);
        advertisement.SetRecursiveDnsServers(
            TimeSpan.FromSeconds(600),
            [IPAddress.Parse("2001:db8:1::1"), IPAddress.Parse("2001:db8:1::2")]
        );
        advertisement.SetSourceLinkLayerAddress([0x00, 0x11, 0x22, 0x33, 0x44, 0x55]);

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(decoded.TryGetMtu(out uint mtu));
        Assert.Equal(1500u, mtu);

        Assert.True(
            decoded.TryGetRecursiveDnsServers(
                out TimeSpan lifetime,
                out IReadOnlyList<IPAddress> dnsServers
            )
        );
        Assert.Equal(TimeSpan.FromSeconds(600), lifetime);
        Assert.Equal(2, dnsServers.Count);
        Assert.Equal(IPAddress.Parse("2001:db8:1::1"), dnsServers[0]);
        Assert.Equal(IPAddress.Parse("2001:db8:1::2"), dnsServers[1]);

        Assert.True(decoded.TryGetSourceLinkLayerAddress(out byte[] linkLayerAddress));
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }, linkLayerAddress);
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_DnsSearchListWithMultipleDomains()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetDnsSearchList(
            TimeSpan.FromSeconds(1800),
            ["example.com", "sub.example.net"]
        );

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        // The option's total length (2-byte header, 6-byte reserved and lifetime, the names) must
        // be a multiple of 8 octets (RFC 4861 Section 4.6).
        Assert.Equal(0, bytes.Length % RaConstants.OptionLengthUnitBytes);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(
            decoded.TryGetDnsSearchList(
                out TimeSpan lifetime,
                out IReadOnlyList<string> domainNames
            )
        );
        Assert.Equal(TimeSpan.FromSeconds(1800), lifetime);
        Assert.Equal(["example.com", "sub.example.net"], domainNames);
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_DnsSearchListWithASingleShortDomain()
    {
        // One short domain name is not a multiple of 8 bytes, so this exercises the zero padding.
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetDnsSearchList(TimeSpan.FromSeconds(600), ["a.io"]);

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(
            decoded.TryGetDnsSearchList(
                out TimeSpan lifetime,
                out IReadOnlyList<string> domainNames
            )
        );
        Assert.Equal(TimeSpan.FromSeconds(600), lifetime);
        Assert.Equal(["a.io"], domainNames);
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_DnsSearchListAlongsideRdnssAndPrefixInformation()
    {
        // With DNSSL among other options (and not last), a length or offset mistake that a
        // single-option test would miss shows up.
        RouterAdvertisementMessage advertisement = new();
        advertisement.AddPrefixInformation(
            new RaPrefixInformationOption
            {
                PrefixLength = 64,
                OnLink = true,
                Autonomous = true,
                ValidLifetime = TimeSpan.FromDays(1),
                PreferredLifetime = TimeSpan.FromHours(12),
                Prefix = IPAddress.Parse("2001:db8:1::"),
            }
        );
        advertisement.SetRecursiveDnsServers(
            TimeSpan.FromSeconds(600),
            [IPAddress.Parse("2001:db8:1::1")]
        );
        advertisement.SetDnsSearchList(TimeSpan.FromSeconds(1800), ["homelab.internal"]);

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(decoded.TryGetPrefixInformationOptions(out _));
        Assert.True(decoded.TryGetRecursiveDnsServers(out _, out _));
        Assert.True(
            decoded.TryGetDnsSearchList(out TimeSpan lifetime, out IReadOnlyList<string> domains)
        );
        Assert.Equal(TimeSpan.FromSeconds(1800), lifetime);
        Assert.Equal(["homelab.internal"], domains);
    }

    [Fact]
    public void RouterAdvertisement_WithNoDnsSearchListOption_TryGetDnsSearchListReturnsFalse()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetMtu(1500);

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.False(decoded.TryGetDnsSearchList(out _, out _));
    }

    [Fact]
    public void SetDnsSearchList_RejectsAnEmptyDomainNameList()
    {
        RouterAdvertisementMessage advertisement = new();

        Assert.Throws<ArgumentException>(() =>
            advertisement.SetDnsSearchList(TimeSpan.FromSeconds(600), [])
        );
    }

    [Fact]
    public void SetDnsSearchList_RejectsAnOverLongLabel()
    {
        RouterAdvertisementMessage advertisement = new();

        Assert.Throws<ArgumentException>(() =>
            advertisement.SetDnsSearchList(
                TimeSpan.FromSeconds(600),
                [new string('a', 64) + ".example.com"]
            )
        );
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("sub.domain.example.com")]
    [InlineData("a.io")]
    public void RaDnsSearchListOption_IsValidDomainName_AcceptsOrdinaryDomains(string domainName)
    {
        Assert.True(RaDnsSearchListOption.IsValidDomainName(domainName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("has..empty.label")]
    public void RaDnsSearchListOption_IsValidDomainName_RejectsMalformedInput(string domainName)
    {
        Assert.False(RaDnsSearchListOption.IsValidDomainName(domainName));
    }

    [Fact]
    public void RaDnsSearchListOption_IsValidDomainName_RejectsAnOverLongLabel()
    {
        Assert.False(RaDnsSearchListOption.IsValidDomainName(new string('a', 64) + ".example.com"));
    }

    [Fact]
    public void RouterAdvertisement_RoundTrips_MultipleRouteInformationOptionsAcrossAllThreeLengthVariants()
    {
        RouterAdvertisementMessage advertisement = new();

        // RFC 4191 Section 2.3's "Length = 1" variant: no prefix octets at all, a
        // default-route-preference-only entry.
        advertisement.AddRouteInformation(
            new RaRouteInformationOption
            {
                PrefixLength = 0,
                Preference = RaRoutePreferenceEnum.Medium,
                RouteLifetime = TimeSpan.FromSeconds(600),
            }
        );

        // RFC 4191 Section 2.3's "Length = 2" variant: an 8-octet prefix, prefix length 1-64.
        advertisement.AddRouteInformation(
            new RaRouteInformationOption
            {
                PrefixLength = 64,
                Preference = RaRoutePreferenceEnum.High,
                RouteLifetime = RaRouteInformationOption.InfiniteLifetime,
                Prefix = IPAddress.Parse("2001:db8:5::"),
            }
        );

        // RFC 4191 Section 2.3's "Length = 3" variant: a 16-octet prefix, prefix length 65-128.
        advertisement.AddRouteInformation(
            new RaRouteInformationOption
            {
                PrefixLength = 100,
                Preference = RaRoutePreferenceEnum.Low,
                RouteLifetime = TimeSpan.FromSeconds(3600),
                Prefix = IPAddress.Parse("2001:db8:6:7::"),
            }
        );

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        // Every option's total length (header plus value) must be a multiple of 8 octets (RFC
        // 4861 Section 4.6).
        Assert.Equal(0, bytes.Length % RaConstants.OptionLengthUnitBytes);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.True(
            decoded.TryGetRouteInformationOptions(
                out IReadOnlyList<RaRouteInformationOption> routes
            )
        );
        Assert.Equal(3, routes.Count);

        Assert.Equal(0, routes[0].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.Medium, routes[0].Preference);
        Assert.Equal(TimeSpan.FromSeconds(600), routes[0].RouteLifetime);
        Assert.Equal(IPAddress.IPv6Any, routes[0].Prefix);

        Assert.Equal(64, routes[1].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.High, routes[1].Preference);
        Assert.Equal(RaRouteInformationOption.InfiniteLifetime, routes[1].RouteLifetime);
        Assert.Equal(IPAddress.Parse("2001:db8:5::"), routes[1].Prefix);

        Assert.Equal(100, routes[2].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.Low, routes[2].Preference);
        Assert.Equal(TimeSpan.FromSeconds(3600), routes[2].RouteLifetime);
        Assert.Equal(IPAddress.Parse("2001:db8:6:7::"), routes[2].Prefix);
    }

    [Fact]
    public void RouterAdvertisement_WithNoRouteInformationOption_TryGetRouteInformationOptionsReturnsFalse()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetMtu(1500);
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.False(decoded.TryGetRouteInformationOptions(out _));
    }

    [Fact]
    public void RouterAdvertisement_WithARouteInformationOptionUsingTheReservedPreferenceValue_SkipsThatOptionOnDecode()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.AddRouteInformation(
            new RaRouteInformationOption
            {
                PrefixLength = 64,
                Preference = RaRoutePreferenceEnum.Medium,
                RouteLifetime = TimeSpan.FromSeconds(600),
                Prefix = IPAddress.Parse("2001:db8:5::"),
            }
        );
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        // The Route Information option starts after the 4-byte ICMPv6 header and the 12-byte
        // body (byte 16 is its Type, byte 17 its Length). Byte 19 holds the preference; set it
        // to the reserved pattern binary 10 (at bits 3-4), which RFC 4191 Section 2.1 says "MUST
        // NOT be sent".
        bytes[19] = 0b0001_0000;

        // The message still decodes; the malformed option is skipped rather than failing the
        // whole message, as with a bad Prefix Information or DNS Search List option.
        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.False(decoded.TryGetRouteInformationOptions(out _));
    }

    [Fact]
    public void RouterAdvertisement_WithARouteInformationOptionWhosePrefixLengthIsInconsistentWithItsOwnOctetCount_SkipsThatOptionOnDecode()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.AddRouteInformation(
            new RaRouteInformationOption
            {
                PrefixLength = 64,
                Preference = RaRoutePreferenceEnum.Medium,
                RouteLifetime = TimeSpan.FromSeconds(600),
                Prefix = IPAddress.Parse("2001:db8:5::"),
            }
        );
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        // Byte 18 is the Prefix Length. Changing 64 (8 prefix octets) to 0 leaves 8 prefix
        // octets for a length the decoder accepts only with none, so the option is skipped.
        bytes[18] = 0;

        Assert.True(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            ),
            failureReason
        );
        Assert.False(decoded.TryGetRouteInformationOptions(out _));
    }

    [Fact]
    public void AddRouteInformation_RejectsAPrefixLengthGreaterThan128()
    {
        RouterAdvertisementMessage advertisement = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            advertisement.AddRouteInformation(
                new RaRouteInformationOption
                {
                    PrefixLength = 200,
                    Prefix = IPAddress.Parse("2001:db8:5::"),
                }
            )
        );
    }

    [Fact]
    public void AddRouteInformation_RejectsAnIPv4Prefix()
    {
        RouterAdvertisementMessage advertisement = new();

        Assert.Throws<ArgumentException>(() =>
            advertisement.AddRouteInformation(
                new RaRouteInformationOption
                {
                    PrefixLength = 24,
                    Prefix = IPAddress.Parse("192.0.2.0"),
                }
            )
        );
    }

    [Fact]
    public void TryReadRouterSolicitation_RejectsWrongIcmpv6Type()
    {
        RouterAdvertisementMessage advertisement = new();
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        Assert.False(
            RaMessageReader.TryReadRouterSolicitation(
                bytes,
                out RouterSolicitationMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryReadRouterAdvertisement_RejectsANonZeroIcmpv6Code()
    {
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(new RouterAdvertisementMessage());
        bytes[1] = 1; // Corrupt the ICMPv6 "code" field, which RFC 4861 requires to be 0.

        Assert.False(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryReadRouterAdvertisement_RejectsATruncatedFixedHeader()
    {
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(new RouterAdvertisementMessage());
        byte[] truncated = bytes[..^1];

        Assert.False(
            RaMessageReader.TryReadRouterAdvertisement(
                truncated,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryReadRouterAdvertisement_RejectsAZeroLengthOption()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetMtu(1500);
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        // Set the MTU option's length field (right after the 12-byte fixed body) to zero, which
        // RFC 4861 Section 4.6 forbids.
        bytes[16 + 1] = 0;

        Assert.False(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryReadRouterAdvertisement_RejectsAnOptionLengthRunningPastTheEndOfTheMessage()
    {
        RouterAdvertisementMessage advertisement = new();
        advertisement.SetMtu(1500);
        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        // Set the MTU option's length field to claim far more data than the datagram carries.
        bytes[16 + 1] = 0xff;

        Assert.False(
            RaMessageReader.TryReadRouterAdvertisement(
                bytes,
                out RouterAdvertisementMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void WriteRouterAdvertisement_RejectsARouterLifetimeThatDoesNotFitInSixteenBits()
    {
        RouterAdvertisementMessage advertisement = new()
        {
            RouterLifetime = TimeSpan.FromSeconds(70000),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RaMessageWriter.WriteRouterAdvertisement(advertisement)
        );
    }
}

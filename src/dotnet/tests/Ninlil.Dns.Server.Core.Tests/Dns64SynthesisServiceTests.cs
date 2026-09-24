using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests <see cref="Dns64SynthesisService" />: RFC 6052 §2.2's own prefix-embedding
///     algorithm across multiple legal prefix lengths, the disabled/enabled gate, and that a
///     synthesized answer's own shape (owner name, class, TTL, CNAME passthrough) matches the real
///     A answer it was derived from.
/// </summary>
public class Dns64SynthesisServiceTests
{
    private static readonly DnsName Name = DnsName.Parse("example.com.");

    private static Dns64SynthesisService CreateService(Dns64Options options) =>
        new(new StaticOptionsMonitor<Dns64Options>(options));

    private static DnsResourceRecord ARecord(string address, uint ttl = 300) =>
        new(
            Name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse(address))
            )
        );

    [Theory]
    // RFC 6052 §2.2's own worked example: the Well-Known Prefix (/96) leaves the IPv4 address's
    // four octets contiguous at the very end, with nothing else to compute.
    [InlineData("64:ff9b::/96", "192.0.2.1", "64:ff9b::c000:201")]
    // A /32 Network-Specific Prefix: the IPv4 address occupies bits 32-63 (right after the
    // prefix), the reserved "u" octet sits at bits 64-71, and the remaining 56 bits are the
    // (here, all-zero) suffix.
    [InlineData("2001:db8::/32", "192.0.2.1", "2001:db8:c000:201::")]
    // A /64 Network-Specific Prefix: the reserved "u" octet sits immediately after the prefix
    // (bits 64-71), and the IPv4 address's four octets follow it contiguously (bits 72-103) --
    // confirmed against RFC 6052 §2.2's own text ("the IPv4 address is encoded in positions 72
    // to 103"), not assumed from the table alone.
    [InlineData("2001:db8:1:2::/64", "192.0.2.1", "2001:db8:1:2:c0:2:100:0")]
    public void EmbedIPv4AddressMatchesRfc6052Section22(
        string prefixCidr,
        string ipv4Address,
        string expectedIPv6Address
    )
    {
        Assert.True(
            Dns64SynthesisService.TryParsePrefix(
                prefixCidr,
                out byte[] prefixBytes,
                out int prefixLength
            )
        );

        byte[] embedded = Dns64SynthesisService.EmbedIPv4Address(
            IPAddress.Parse(ipv4Address),
            prefixBytes,
            prefixLength
        );

        Assert.Equal(IPAddress.Parse(expectedIPv6Address), new IPAddress(embedded));
    }

    [Theory]
    [InlineData("64:ff9b::/96", true)]
    [InlineData("2001:db8::/32", true)]
    [InlineData("2001:db8::/40", true)]
    [InlineData("2001:db8::/48", true)]
    [InlineData("2001:db8::/56", true)]
    [InlineData("2001:db8::/64", true)]
    // RFC 6052 §2.2 defines exactly six legal prefix lengths -- anything else must be rejected.
    [InlineData("2001:db8::/48/extra", false)]
    [InlineData("2001:db8::/33", false)]
    [InlineData("2001:db8::/128", false)]
    // An IPv4 address (or anything else that isn't a well-formed IPv6 CIDR) is never a legal
    // DNS64 prefix.
    [InlineData("192.0.2.0/24", false)]
    [InlineData("not-a-prefix", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParsePrefixOnlyAcceptsTheSixLegalRfc6052Lengths(string cidr, bool expected)
    {
        bool actual = Dns64SynthesisService.TryParsePrefix(cidr, out _, out _);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParsePrefixMasksHostBitsBeyondThePrefixLength()
    {
        // A non-network address (host bits already set) must still be masked down to a real
        // network address -- a misconfigured operator-supplied value should never leak stray
        // bits into every synthesized address's own suffix.
        Assert.True(
            Dns64SynthesisService.TryParsePrefix(
                "2001:db8::1234/32",
                out byte[] prefixBytes,
                out int prefixLength
            )
        );

        Assert.Equal(32, prefixLength);
        Assert.Equal(IPAddress.Parse("2001:db8::"), new IPAddress(prefixBytes));
    }

    [Fact]
    public void SynthesizeAaaaRecordsReturnsNothingWhenDisabled()
    {
        Dns64SynthesisService service = CreateService(new Dns64Options { Enable = false });

        IReadOnlyList<DnsResourceRecord> synthesized = service.SynthesizeAaaaRecords([
            ARecord("192.0.2.1"),
        ]);

        Assert.Empty(synthesized);
    }

    [Fact]
    public void SynthesizeAaaaRecordsReturnsNothingForAnEmptyAAnswer()
    {
        Dns64SynthesisService service = CreateService(new Dns64Options { Enable = true });

        Assert.Empty(service.SynthesizeAaaaRecords([]));
    }

    [Fact]
    public void SynthesizeAaaaRecordsReturnsNothingWhenThePrefixIsMisconfigured()
    {
        // Fails safe -- no synthesized answer -- rather than throwing mid-query.
        Dns64SynthesisService service = CreateService(
            new Dns64Options { Enable = true, SynthesisPrefix = "not-a-prefix" }
        );

        Assert.Empty(service.SynthesizeAaaaRecords([ARecord("192.0.2.1")]));
    }

    [Fact]
    public void SynthesizeAaaaRecordsBuildsARealAaaaAnswerWhenEnabled()
    {
        Dns64SynthesisService service = CreateService(
            new Dns64Options { Enable = true, SynthesisPrefix = "64:ff9b::/96" }
        );

        IReadOnlyList<DnsResourceRecord> synthesized = service.SynthesizeAaaaRecords([
            ARecord("192.0.2.1", ttl: 120),
        ]);

        DnsResourceRecord record = Assert.Single(synthesized);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, record.Type);
        Assert.Equal(Name, record.Name);
        Assert.Equal(DnsClassEnum.Internet, record.Class);
        // RFC 6147 §6.1: a synthesized RRset's own TTL should be no greater than the real A
        // record's own TTL.
        Assert.Equal((uint)120, record.TimeToLive);

        Assert.True(
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Aaaa, record.Data)
                is AaaaRecordData
        );
        AaaaRecordData data = (AaaaRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Aaaa, record.Data);
        Assert.Equal(IPAddress.Parse("64:ff9b::c000:201"), data.Address);
    }

    [Fact]
    public void SynthesizeAaaaRecordsPreservesALeadingCnameHopUnchanged()
    {
        DnsResourceRecord cname = new(
            Name,
            DnsResourceRecordTypeEnum.Cname,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Cname,
                new CnameRecordData(DnsName.Parse("target.example.com."))
            )
        );
        DnsResourceRecord aRecord = new(
            DnsName.Parse("target.example.com."),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse("192.0.2.1"))
            )
        );

        Dns64SynthesisService service = CreateService(
            new Dns64Options { Enable = true, SynthesisPrefix = "64:ff9b::/96" }
        );

        IReadOnlyList<DnsResourceRecord> synthesized = service.SynthesizeAaaaRecords([
            cname,
            aRecord,
        ]);

        Assert.Equal(2, synthesized.Count);
        Assert.Same(cname, synthesized[0]);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, synthesized[1].Type);
        Assert.Equal(DnsName.Parse("target.example.com."), synthesized[1].Name);
    }
}

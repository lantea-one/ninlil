using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests <see cref="Dns64SynthesisService" /> with a fixed options monitor: the
///     RFC 6052 §2.2 embedding for several prefix lengths, prefix parsing, the enable switch, and
///     that a synthesized answer keeps the owner name, class, TTL and CNAME records of the A
///     answer it came from.
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
    // The Well-Known Prefix (/96): the IPv4 address fills the last four octets.
    [InlineData("64:ff9b::/96", "192.0.2.1", "64:ff9b::c000:201")]
    // A /32 prefix: the IPv4 address takes bits 32 to 63, the "u" octet bits 64 to 71, and the
    // remaining 56 bits are the zero suffix.
    [InlineData("2001:db8::/32", "192.0.2.1", "2001:db8:c000:201::")]
    // A /64 prefix: the "u" octet takes bits 64 to 71 and the IPv4 address bits 72 to 103, as
    // RFC 6052 §2.2 states.
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
    // RFC 6052 §2.2 defines six prefix lengths; anything else is rejected.
    [InlineData("2001:db8::/48/extra", false)]
    [InlineData("2001:db8::/33", false)]
    [InlineData("2001:db8::/128", false)]
    // Anything that is not an IPv6 CIDR, including an IPv4 one, is rejected.
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
        // Bits after the prefix length are cleared, so a sloppy configured value cannot leak
        // stray bits into synthesized addresses.
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
        // An invalid prefix gives no synthesized answer rather than an exception.
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
        // RFC 6147 §5.1.7: the synthesized TTL should not exceed the A record's TTL.
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

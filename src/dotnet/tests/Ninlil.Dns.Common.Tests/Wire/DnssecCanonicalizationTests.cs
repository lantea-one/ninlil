using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnssecCanonicalization" />: RFC 4034 §6's canonical form and
///     ordering rules that every DNSSEC signature depends on.
/// </summary>
public class DnssecCanonicalizationTests
{
    /// <summary>
    ///     This test reproduces RFC 4034 §6.1's own worked example of canonical DNS name order
    ///     verbatim, after shuffling it, to confirm <see cref="DnssecCanonicalization.CompareCanonicalOrder" />
    ///     reconstructs exactly the RFC's own ordering rather than merely a plausible one.
    /// </summary>
    [Fact]
    public void CompareCanonicalOrderReproducesRfc4034WorkedExample()
    {
        string[] rfc4034Example =
        [
            "example",
            "a.example",
            "yljkjljk.a.example",
            "Z.a.example",
            "zABC.a.EXAMPLE",
            "z.example",
            "\\001.z.example",
            "*.z.example",
            "\\200.z.example",
        ];
        List<DnsName> expectedOrder = [.. rfc4034Example.Select(DnsName.Parse)];

        List<DnsName> shuffled = [.. expectedOrder];
        (shuffled[0], shuffled[^1]) = (shuffled[^1], shuffled[0]);
        (shuffled[2], shuffled[5]) = (shuffled[5], shuffled[2]);
        (shuffled[3], shuffled[7]) = (shuffled[7], shuffled[3]);

        List<DnsName> resorted =
        [
            .. shuffled.OrderBy(
                name => name,
                Comparer<DnsName>.Create(DnssecCanonicalization.CompareCanonicalOrder)
            ),
        ];

        Assert.Equal(expectedOrder, resorted);
    }

    [Fact]
    public void CanonicalOrderSortsAnRRsetByRdataAsAnUnsignedOctetSequence()
    {
        DnsName owner = DnsName.Parse("www.example.com");
        List<DnsResourceRecord> unordered =
        [
            new DnsResourceRecord(
                owner,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                3600,
                [192, 0, 2, 200]
            ),
            new DnsResourceRecord(
                owner,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                3600,
                [10, 0, 0, 1]
            ),
            new DnsResourceRecord(
                owner,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                3600,
                [10, 0, 0, 2]
            ),
        ];

        List<DnsResourceRecord> ordered = DnssecCanonicalization.CanonicalOrder(unordered);

        Assert.Equal([10, 10, 192], ordered.Select(r => (int)r.Data[0]));
        Assert.Equal(1, ordered[0].Data[3]);
        Assert.Equal(2, ordered[1].Data[3]);
    }

    [Fact]
    public void ToCanonicalRdataLowercasesAnEmbeddedNameForApplicableTypes()
    {
        NsRecordData nsData = new(DnsName.Parse("NS1.EXAMPLE.COM"));
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.NS, nsData);

        byte[] canonicalRdata = DnssecCanonicalization.ToCanonicalRdata(
            DnsResourceRecordTypeEnum.NS,
            rdata
        );
        NsRecordData canonicalDecoded = (NsRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.NS, canonicalRdata);

        Assert.Equal("ns1.example.com.", canonicalDecoded.NameServer.ToString());
    }

    [Fact]
    public void ToCanonicalRdataLeavesRdataWithNoEmbeddedNameByteIdenticalButCopied()
    {
        byte[] aRdata = [192, 0, 2, 1];

        byte[] canonicalRdata = DnssecCanonicalization.ToCanonicalRdata(
            DnsResourceRecordTypeEnum.A,
            aRdata
        );

        Assert.Equal(aRdata, canonicalRdata);
        Assert.NotSame(aRdata, canonicalRdata);
    }

    /// <summary>
    ///     This test hand-assembles the exact bytes RFC 4034 §3.1.8.1 defines as a signature's
    ///     input for a single-record RRset, and confirms <see cref="DnssecCanonicalization.BuildSigningInput" />
    ///     produces them byte for byte.
    /// </summary>
    [Fact]
    public void BuildSigningInputProducesTheExactHandAssembledBytes()
    {
        DnsName owner = DnsName.Parse("HOST.Example.com");
        DnsResourceRecord aRecord = new(
            owner,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            [192, 0, 2, 1]
        );
        RrSigRecordData metadata = new(
            DnsResourceRecordTypeEnum.A,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            3,
            300,
            2000000000,
            1900000000,
            9999,
            DnsName.Parse("example.com"),
            [] // Ignored -- the signature has not been computed yet.
        );

        byte[] signingInput = DnssecCanonicalization.BuildSigningInput(
            metadata,
            owner,
            DnsClassEnum.Internet,
            [aRecord]
        );

        byte[] expectedRrsigRdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.RrSig,
            new RrSigRecordData(
                DnsResourceRecordTypeEnum.A,
                DnssecAlgorithmEnum.EcdsaP256Sha256,
                3,
                300,
                2000000000,
                1900000000,
                9999,
                DnsName.Parse("example.com"),
                []
            )
        );

        List<byte> expected = [.. expectedRrsigRdata];
        expected.AddRange([4, (byte)'h', (byte)'o', (byte)'s', (byte)'t', 7]);
        expected.AddRange("example"u8.ToArray());
        expected.Add(3);
        expected.AddRange("com"u8.ToArray());
        expected.Add(0); // Root label terminating the lower-cased owner name.
        expected.AddRange([0, 1]); // TYPE = A
        expected.AddRange([0, 1]); // CLASS = IN
        expected.AddRange([0, 0, 1, 44]); // Original TTL = 300
        expected.AddRange([0, 4]); // RDLENGTH = 4
        expected.AddRange([192, 0, 2, 1]); // RDATA

        Assert.Equal(expected, signingInput);
    }
}

using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnssecCanonicalization" />, the canonical form and ordering
///     rules of RFC 4034 §6 that every DNSSEC signature depends on. Expected values are taken from
///     the RFC or assembled by hand in the test.
/// </summary>
public class DnssecCanonicalizationTests
{
    /// <summary>
    ///     This test shuffles the worked example of canonical name order from RFC 4034 §6.1 and
    ///     checks that <see cref="DnssecCanonicalization.CompareCanonicalOrder" /> restores the RFC's
    ///     order.
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
    ///     This test assembles by hand the signature input of RFC 4034 §3.1.8.1 for a single-record
    ///     RRset and checks that <see cref="DnssecCanonicalization.BuildSigningInput" /> produces the
    ///     same bytes.
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
            [] // Ignored: the signature has not been computed yet.
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

    [Fact]
    public void ToCanonicalRdataLeavesAnNsecNextDomainNameInItsOriginalCase()
    {
        // RFC 6840 §5.1: the NSEC Next Domain Name is not lower-cased in canonical form.
        byte[] rdata =
        [
            .. DnsName.Parse("B.Example.").ToUncompressedWireBytes(),
            0,
            6,
            0x40,
            0,
            0,
            0,
            0,
            3,
        ];

        byte[] canonicalRdata = DnssecCanonicalization.ToCanonicalRdata(
            DnsResourceRecordTypeEnum.Nsec,
            rdata
        );

        Assert.Equal(rdata, canonicalRdata);
    }

    [Fact]
    public void BuildSigningInputWritesTheSignerNameInLowerCase()
    {
        DnsName owner = DnsName.Parse("www.example.com");
        DnsResourceRecord aRecord = new(
            owner,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            [192, 0, 2, 1]
        );
        RrSigRecordData mixedCase = new(
            DnsResourceRecordTypeEnum.A,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            3,
            300,
            2000000000,
            1900000000,
            9999,
            DnsName.Parse("Example.COM."),
            []
        );
        RrSigRecordData lowerCase = new(
            mixedCase.TypeCovered,
            mixedCase.Algorithm,
            mixedCase.Labels,
            mixedCase.OriginalTimeToLiveSeconds,
            mixedCase.SignatureExpiration,
            mixedCase.SignatureInception,
            mixedCase.KeyTag,
            DnsName.Parse("example.com."),
            []
        );

        Assert.Equal(
            DnssecCanonicalization.BuildSigningInput(
                lowerCase,
                owner,
                DnsClassEnum.Internet,
                [aRecord]
            ),
            DnssecCanonicalization.BuildSigningInput(
                mixedCase,
                owner,
                DnsClassEnum.Internet,
                [aRecord]
            )
        );
    }

    [Fact]
    public void ReconstructSignedOwnerRebuildsAWildcardFromTheRightmostLabels()
    {
        Assert.Equal(
            DnsName.Parse("*.example."),
            DnssecCanonicalization.ReconstructSignedOwner(DnsName.Parse("foo.bar.example."), 1)
        );
    }

    [Fact]
    public void ReconstructSignedOwnerKeepsAWildcardOwnerWhoseLabelsExcludeTheAsterisk()
    {
        Assert.Equal(
            DnsName.Parse("*.example."),
            DnssecCanonicalization.ReconstructSignedOwner(DnsName.Parse("*.example."), 1)
        );
    }

    [Fact]
    public void ReconstructSignedOwnerKeepsTheOwnerWhenLabelsMatchItsLabelCount()
    {
        DnsName owner = DnsName.Parse("www.example.com.");

        Assert.Equal(owner, DnssecCanonicalization.ReconstructSignedOwner(owner, 3));
    }

    [Fact]
    public void ReconstructSignedOwnerReturnsNullWhenLabelsExceedTheLabelCount()
    {
        Assert.Null(DnssecCanonicalization.ReconstructSignedOwner(DnsName.Parse("example."), 2));
    }

    [Fact]
    public void ReconstructSignedOwnerOfALabelsValueOfZeroIsTheRootWildcard()
    {
        Assert.Equal(
            DnsName.Parse("*."),
            DnssecCanonicalization.ReconstructSignedOwner(DnsName.Parse("a.b."), 0)
        );
    }

    [Fact]
    public void BuildSigningInputForAWildcardExpansionUsesTheWildcardOwner()
    {
        RrSigRecordData metadata = new(
            DnsResourceRecordTypeEnum.A,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            2,
            300,
            2000000000,
            1900000000,
            9999,
            DnsName.Parse("example.com"),
            []
        );
        DnsResourceRecord expanded = new(
            DnsName.Parse("X.y.example.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            [192, 0, 2, 1]
        );
        DnsResourceRecord atWildcard = new(
            DnsName.Parse("*.example.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            [192, 0, 2, 1]
        );

        Assert.Equal(
            DnssecCanonicalization.BuildSigningInput(
                metadata,
                atWildcard.Name,
                DnsClassEnum.Internet,
                [atWildcard]
            ),
            DnssecCanonicalization.BuildSigningInput(
                metadata,
                expanded.Name,
                DnsClassEnum.Internet,
                [expanded]
            )
        );
    }

    [Fact]
    public void BuildSigningInputThrowsWhenLabelsExceedTheOwnerLabelCount()
    {
        RrSigRecordData metadata = new(
            DnsResourceRecordTypeEnum.A,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            5,
            300,
            2000000000,
            1900000000,
            9999,
            DnsName.Parse("example.com"),
            []
        );
        DnsResourceRecord record = new(
            DnsName.Parse("example.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            [192, 0, 2, 1]
        );

        Assert.Throws<ArgumentException>(() =>
            DnssecCanonicalization.BuildSigningInput(
                metadata,
                record.Name,
                DnsClassEnum.Internet,
                [record]
            )
        );
    }
}

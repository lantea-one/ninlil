using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="DenialOfExistenceValidator" />, the structural half of
///     authenticated denial of existence (RFC 4035 §5.4, RFC 5155 §8), with hand-built NSEC and
///     NSEC3 records and no signatures. NSEC3 chains are built the way a signer does: every name
///     hashed, sorted and linked, so a name outside the chain is covered by exactly one record.
/// </summary>
public class DenialOfExistenceValidatorTests
{
    private static readonly DnsName Apex = DnsName.Parse("example.com");
    private static readonly DnsName ShortApex = DnsName.Parse("example");

    private const DenialOfExistenceOutcomeEnum Proven = DenialOfExistenceOutcomeEnum.Proven;
    private const DenialOfExistenceOutcomeEnum NotProven = DenialOfExistenceOutcomeEnum.NotProven;
    private const DenialOfExistenceOutcomeEnum ProvenInsecure =
        DenialOfExistenceOutcomeEnum.ProvenInsecure;

    private static readonly DnsResourceRecordTypeEnum[] ApexTypes =
    [
        DnsResourceRecordTypeEnum.Soa,
        DnsResourceRecordTypeEnum.NS,
    ];

    private static DnsResourceRecord BuildNsec(
        string owner,
        string next,
        params DnsResourceRecordTypeEnum[] types
    ) =>
        new(
            DnsName.Parse(owner),
            DnsResourceRecordTypeEnum.Nsec,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec,
                new NsecRecordData(DnsName.Parse(next), types)
            )
        );

    private static DnsResourceRecord BuildNsec3(
        byte[] ownerHash,
        byte[] nextHash,
        bool isOptOut,
        byte hashAlgorithm,
        ushort iterations,
        params DnsResourceRecordTypeEnum[] types
    ) =>
        new(
            Nsec3Hasher.ToOwnerName(ownerHash, Apex),
            DnsResourceRecordTypeEnum.Nsec3,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec3,
                new Nsec3RecordData(hashAlgorithm, isOptOut, iterations, [], nextHash, types)
            )
        );

    private static byte[] Hash(string name, ushort iterations = 0) =>
        Nsec3Hasher.ComputeHash(1, DnsName.Parse(name), iterations, []);

    /// <summary>
    ///     This method builds a complete NSEC3 chain for <c>example.com</c>: one record per name,
    ///     sorted by hash, each pointing at the next and the last wrapping to the first.
    /// </summary>
    private static List<DnsResourceRecord> BuildChain(
        bool isOptOut,
        ushort iterations,
        params (string Name, DnsResourceRecordTypeEnum[] Types)[] names
    )
    {
        List<(byte[] Hash, DnsResourceRecordTypeEnum[] Types)> items =
        [
            .. names
                .Select(entry => (Hash(entry.Name, iterations), entry.Types))
                .OrderBy(entry => Convert.ToHexString(entry.Item1), StringComparer.Ordinal),
        ];

        return
        [
            .. items.Select(
                (item, index) =>
                    BuildNsec3(
                        item.Hash,
                        items[(index + 1) % items.Count].Hash,
                        isOptOut,
                        1,
                        iterations,
                        item.Types
                    )
            ),
        ];
    }

    private sealed class HashCounter
    {
        public int Count { get; private set; }

        public byte[] Hash(byte hashAlgorithm, DnsName name, ushort iterations, byte[] salt)
        {
            Count++;
            return Nsec3Hasher.ComputeHash(hashAlgorithm, name, iterations, salt);
        }
    }

    // ---------------------------------------------------------------- NSEC NODATA

    [Fact]
    public void VerifyNoData_NsecAtTheNameWithoutTheType_IsProven()
    {
        DnsResourceRecord nsec = BuildNsec(
            "alpha.example",
            "omega.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("alpha.example"),
                DnsResourceRecordTypeEnum.Aaaa,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_NsecAtTheNameWithTheType_IsNotProven()
    {
        DnsResourceRecord nsec = BuildNsec(
            "alpha.example",
            "omega.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("alpha.example"),
                DnsResourceRecordTypeEnum.A,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_NsecWithACnameBit_IsNotProven()
    {
        // A name with a CNAME answers every type with the CNAME, so "no AAAA" is false.
        DnsResourceRecord nsec = BuildNsec(
            "alpha.example",
            "omega.example",
            DnsResourceRecordTypeEnum.A,
            DnsResourceRecordTypeEnum.Cname
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("alpha.example"),
                DnsResourceRecordTypeEnum.Aaaa,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_DsQueryAtADelegationWithoutSoaOrDs_IsProven()
    {
        DnsResourceRecord nsec = BuildNsec(
            "child.example",
            "zzz.example",
            DnsResourceRecordTypeEnum.NS
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("child.example"),
                DnsResourceRecordTypeEnum.DS,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_DsQueryAnsweredByTheChildApexNsec_IsNotProven()
    {
        // NS and SOA together mark the child side of the cut. The parent holds the DS, so this
        // NSEC cannot show the delegation is unsigned (RFC 6840 §4.4).
        DnsResourceRecord nsec = BuildNsec(
            "child.example",
            "zzz.example",
            DnsResourceRecordTypeEnum.NS,
            DnsResourceRecordTypeEnum.Soa,
            DnsResourceRecordTypeEnum.DnsKey
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("child.example"),
                DnsResourceRecordTypeEnum.DS,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_DsQueryAtANameThatIsNotADelegation_IsNotProven()
    {
        DnsResourceRecord nsec = BuildNsec(
            "plain.example",
            "zzz.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("plain.example"),
                DnsResourceRecordTypeEnum.DS,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_NonDsQueryAnsweredByTheParentSideOfADelegation_IsNotProven()
    {
        DnsResourceRecord nsec = BuildNsec(
            "child.example",
            "zzz.example",
            DnsResourceRecordTypeEnum.NS
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("child.example"),
                DnsResourceRecordTypeEnum.A,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_EmptyNonTerminal_IsProven()
    {
        // b.example has no records, but c.b.example does, so the NSEC a.example -> c.b.example
        // covers b.example and its next name is below it.
        DnsResourceRecord nsec = BuildNsec("a.example", "c.b.example", DnsResourceRecordTypeEnum.A);

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("b.example"),
                DnsResourceRecordTypeEnum.A,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_WildcardWithoutTheType_IsProven()
    {
        // b.example does not exist and *.example holds only A, so AAAA at b.example is NODATA
        // (RFC 4035 §3.1.3.4).
        DnsResourceRecord covering = BuildNsec(
            "a.example",
            "z.example",
            DnsResourceRecordTypeEnum.A
        );
        DnsResourceRecord wildcard = BuildNsec(
            "*.example",
            "a.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("b.example"),
                DnsResourceRecordTypeEnum.Aaaa,
                ShortApex,
                [covering, wildcard]
            )
        );
    }

    [Fact]
    public void VerifyNoData_WildcardThatHasTheType_IsNotProven()
    {
        DnsResourceRecord covering = BuildNsec(
            "a.example",
            "z.example",
            DnsResourceRecordTypeEnum.A
        );
        DnsResourceRecord wildcard = BuildNsec(
            "*.example",
            "a.example",
            DnsResourceRecordTypeEnum.A,
            DnsResourceRecordTypeEnum.Aaaa
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("b.example"),
                DnsResourceRecordTypeEnum.Aaaa,
                ShortApex,
                [covering, wildcard]
            )
        );
    }

    [Fact]
    public void VerifyNoData_NoRecordForTheName_IsNotProven()
    {
        DnsResourceRecord nsec = BuildNsec(
            "alpha.example",
            "omega.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("zzz.example"),
                DnsResourceRecordTypeEnum.A,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNoData_NsecOwnedOutsideTheZone_IsIgnored()
    {
        // The name is in the zone below sub.example, but the proof comes from the parent zone
        // example, which cannot speak for it.
        DnsResourceRecord nsec = BuildNsec(
            "www.sub.example",
            "zzz.sub.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("www.sub.example"),
                DnsResourceRecordTypeEnum.Aaaa,
                DnsName.Parse("other.example"),
                [nsec]
            )
        );
        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("www.sub.example"),
                DnsResourceRecordTypeEnum.Aaaa,
                DnsName.Parse("deeper.www.sub.example"),
                [nsec]
            )
        );
    }

    // ---------------------------------------------------------------- NSEC NXDOMAIN

    [Fact]
    public void VerifyNxDomain_NameAndWildcardCovered_IsProven()
    {
        DnsResourceRecord apexNsec = BuildNsec(
            "example",
            "aardvark.example",
            DnsResourceRecordTypeEnum.Soa
        );
        DnsResourceRecord aardvarkNsec = BuildNsec(
            "aardvark.example",
            "mango.example",
            DnsResourceRecordTypeEnum.A
        );
        DnsResourceRecord mangoNsec = BuildNsec(
            "mango.example",
            "example",
            DnsResourceRecordTypeEnum.A
        );

        // The wildcard *.example sorts between the apex and aardvark, so the apex NSEC covers it.
        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("banana.example"),
                ShortApex,
                [apexNsec, aardvarkNsec, mangoNsec]
            )
        );
    }

    [Fact]
    public void VerifyNxDomain_NoNsecCoversTheName_IsNotProven()
    {
        DnsResourceRecord apexNsec = BuildNsec(
            "example",
            "aardvark.example",
            DnsResourceRecordTypeEnum.Soa
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("zebra.example"),
                ShortApex,
                [apexNsec]
            )
        );
    }

    [Fact]
    public void VerifyNxDomain_ClosestEncloserIsAnEmptyNonTerminal_IsProven()
    {
        // The zone holds a.example and x.c.example, so c.example is an empty non-terminal with no
        // NSEC of its own. The one NSEC a.example -> x.c.example covers both b.c.example and
        // *.c.example, and the closest encloser c.example comes from its next name.
        DnsResourceRecord nsec = BuildNsec("a.example", "x.c.example", DnsResourceRecordTypeEnum.A);

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("b.c.example"),
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNxDomain_WildcardExistsAtTheClosestEncloser_IsNotProven()
    {
        // *.example is a real name, so b.example may be answered by it.
        DnsResourceRecord covering = BuildNsec(
            "a.example",
            "c.example",
            DnsResourceRecordTypeEnum.A
        );
        DnsResourceRecord wildcard = BuildNsec(
            "*.example",
            "a.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("b.example"),
                ShortApex,
                [covering, wildcard]
            )
        );
    }

    [Fact]
    public void VerifyNxDomain_NameThatIsAnEmptyNonTerminal_IsNotProven()
    {
        // b.example exists as an empty non-terminal (c.b.example is next), so it is not NXDOMAIN.
        DnsResourceRecord nsec = BuildNsec("a.example", "c.b.example", DnsResourceRecordTypeEnum.A);

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomain(DnsName.Parse("b.example"), ShortApex, [nsec])
        );
    }

    [Fact]
    public void VerifyNxDomain_NsecOwnedOutsideTheZone_IsIgnored()
    {
        DnsResourceRecord parentNsec = BuildNsec(
            "a.example",
            "z.example",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("x.sub.example"),
                DnsName.Parse("sub.example"),
                [parentNsec]
            )
        );
    }

    [Fact]
    public void VerifyNxDomain_CoveredOnlyByADelegationAboveTheName_IsNotProven()
    {
        // d.example is a delegation. Its NSEC says nothing about x.d.example, which belongs to the
        // child zone (RFC 6840 §4.1).
        DnsResourceRecord delegation = BuildNsec(
            "d.example",
            "z.example",
            DnsResourceRecordTypeEnum.NS
        );
        DnsResourceRecord start = BuildNsec("example", "d.example", DnsResourceRecordTypeEnum.Soa);

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("x.d.example"),
                ShortApex,
                [delegation, start]
            )
        );
    }

    // ---------------------------------------------------------------- NSEC wildcard answer

    [Fact]
    public void VerifyWildcardAnswer_NextCloserNameCovered_IsProven()
    {
        // *.example answered a.b.example (RRSIG Labels = 1); b.example must not exist.
        DnsResourceRecord nsec = BuildNsec("a.example", "c.example", DnsResourceRecordTypeEnum.A);

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyWildcardAnswer(
                DnsName.Parse("a.b.example"),
                1,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyWildcardAnswer_NextCloserNameExists_IsNotProven()
    {
        DnsResourceRecord nsec = BuildNsec("b.example", "c.example", DnsResourceRecordTypeEnum.A);

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyWildcardAnswer(
                DnsName.Parse("a.b.example"),
                1,
                ShortApex,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyWildcardAnswer_LabelsNotBelowTheQueriedName_IsNotProven()
    {
        DnsResourceRecord nsec = BuildNsec("a.example", "c.example", DnsResourceRecordTypeEnum.A);

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyWildcardAnswer(
                DnsName.Parse("a.b.example"),
                3,
                ShortApex,
                [nsec]
            )
        );
    }

    // ---------------------------------------------------------------- NSEC3

    [Fact]
    public void VerifyNoDataNsec3_ExactMatchLackingTheType_IsProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("delegated.example.com", [DnsResourceRecordTypeEnum.NS])
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("delegated.example.com"),
                DnsResourceRecordTypeEnum.DS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_ExactMatchWithTheType_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("delegated.example.com", [DnsResourceRecordTypeEnum.NS])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("delegated.example.com"),
                DnsResourceRecordTypeEnum.NS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_ExactMatchWithACnameBit_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("alias.example.com", [DnsResourceRecordTypeEnum.Cname])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("alias.example.com"),
                DnsResourceRecordTypeEnum.Aaaa,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_DsQueryAnsweredByTheChildApex_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("child.example.com", ApexTypes)
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("child.example.com"),
                DnsResourceRecordTypeEnum.DS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_WildcardWithoutTheType_IsProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("*.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("nope.example.com"),
                DnsResourceRecordTypeEnum.Aaaa,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_WildcardWithTheType_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("*.example.com", [DnsResourceRecordTypeEnum.A, DnsResourceRecordTypeEnum.Aaaa])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("nope.example.com"),
                DnsResourceRecordTypeEnum.Aaaa,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_OptOutDsProofUnderAnEmptyNonTerminal_IsProvenInsecure()
    {
        // ent.example.com is an empty non-terminal (it has an NSEC3 with an empty bitmap). The
        // unsigned delegation child.ent.example.com has no NSEC3; the Opt-Out span covers it.
        List<DnsResourceRecord> chain = BuildChain(
            true,
            0,
            ("example.com", ApexTypes),
            ("ent.example.com", []),
            ("leaf.other.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            ProvenInsecure,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("child.ent.example.com"),
                DnsResourceRecordTypeEnum.DS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_DsProofWithoutOptOutAndWithoutAMatch_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("ent.example.com", [])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("child.ent.example.com"),
                DnsResourceRecordTypeEnum.DS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_OptOutSpanWhoseRangeDoesNotCoverTheName_IsNotProven()
    {
        // The Opt-Out flag only counts on the record whose range covers the next closer name.
        byte[] childHash = Hash("child.example.com");
        byte[] rangeStart = (byte[])childHash.Clone();
        rangeStart[0]++;
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();

        // The range that does not cover the next closer name.
        List<DnsResourceRecord> chain =
        [
            BuildNsec3(Hash("example.com"), rangeStart, false, 1, 0, ApexTypes),
            BuildNsec3(rangeStart, allFf, true, 1, 0, DnsResourceRecordTypeEnum.NS),
        ];

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("child.example.com"),
                DnsResourceRecordTypeEnum.DS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3_ClosestEncloserIsADelegation_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            true,
            0,
            ("example.com", ApexTypes),
            ("d.example.com", [DnsResourceRecordTypeEnum.NS])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("child.d.example.com"),
                DnsResourceRecordTypeEnum.DS,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_ClosestEncloserNextCloserAndWildcard_IsProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_HandBuiltCatchAllRange_IsProven()
    {
        // A range from all-zero to all-0xFF covers every SHA-1 hash, so it proves the next closer
        // name and the wildcard absent at once.
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        DnsResourceRecord exactMatch = BuildNsec3(
            Hash("example.com"),
            allZero,
            false,
            1,
            0,
            ApexTypes
        );
        DnsResourceRecord catchAll = BuildNsec3(
            allZero,
            allFf,
            false,
            1,
            0,
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                [exactMatch, catchAll]
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_NoClosestEncloserMatch_IsNotProven()
    {
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        DnsResourceRecord catchAll = BuildNsec3(
            allZero,
            allFf,
            false,
            1,
            0,
            DnsResourceRecordTypeEnum.A
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                [catchAll]
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_ClosestEncloserIsADelegation_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("d.example.com", [DnsResourceRecordTypeEnum.NS])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("x.d.example.com"),
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_NameThatMatchesARecord_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("here.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("here.example.com"),
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_NextCloserCoveredByAnOptOutRecord_IsProvenInsecure()
    {
        List<DnsResourceRecord> chain = BuildChain(
            true,
            0,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            ProvenInsecure,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyWildcardAnswerNsec3_NextCloserNameCovered_IsProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("*.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyWildcardAnswerNsec3(
                DnsName.Parse("a.b.example.com"),
                2,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyWildcardAnswerNsec3_NextCloserNameExists_IsNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("b.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyWildcardAnswerNsec3(
                DnsName.Parse("a.b.example.com"),
                2,
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_IterationsAboveTheCap_IsProvenInsecureWithoutHashing()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            151,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );
        HashCounter counter = new();

        Assert.Equal(
            ProvenInsecure,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                chain,
                counter.Hash
            )
        );
        Assert.Equal(
            ProvenInsecure,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse("mail.example.com"),
                DnsResourceRecordTypeEnum.Aaaa,
                Apex,
                chain,
                counter.Hash
            )
        );
        Assert.Equal(
            ProvenInsecure,
            DenialOfExistenceValidator.VerifyWildcardAnswerNsec3(
                DnsName.Parse("a.b.example.com"),
                2,
                Apex,
                chain,
                counter.Hash
            )
        );
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void VerifyNxDomainNsec3_IterationsAtTheCap_AreHashedAndProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            DenialOfExistenceValidator.MaxNsec3Iterations,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );
        HashCounter counter = new();

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                chain,
                counter.Hash
            )
        );
        Assert.True(counter.Count > 0);
    }

    [Fact]
    public void VerifyNxDomainNsec3_RecordWithHashAlgorithmTwo_IsIgnoredWithoutAnException()
    {
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        DnsResourceRecord unknownAlgorithm = BuildNsec3(
            allZero,
            allFf,
            false,
            2,
            0,
            DnsResourceRecordTypeEnum.A
        );

        // Alone, the record leaves nothing to prove with.
        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                [unknownAlgorithm]
            )
        );

        // Next to a valid chain, it does not disturb the proof.
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );
        chain.Add(unknownAlgorithm);

        Assert.Equal(
            Proven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_RecordsThatDisagreeOnIterations_AreNotProven()
    {
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );
        chain.Add(
            BuildNsec3(
                Hash("other.example.com", 5),
                Hash("example.com", 5),
                false,
                1,
                5,
                DnsResourceRecordTypeEnum.A
            )
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.example.com"),
                Apex,
                chain
            )
        );
    }

    [Fact]
    public void VerifyNxDomainNsec3_RecordsOwnedOutsideTheZone_AreIgnored()
    {
        // The chain belongs to example.com; the check is for a name in other.com.
        List<DnsResourceRecord> chain = BuildChain(
            false,
            0,
            ("example.com", ApexTypes),
            ("mail.example.com", [DnsResourceRecordTypeEnum.A])
        );

        Assert.Equal(
            NotProven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse("a.b.other.com"),
                DnsName.Parse("other.com"),
                chain
            )
        );
    }
}

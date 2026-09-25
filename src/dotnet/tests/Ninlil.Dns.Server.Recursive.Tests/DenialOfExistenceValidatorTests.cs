using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="DenialOfExistenceValidator" />, the structural half of
///     authenticated denial of existence (RFC 4035 §5.4, RFC 5155 §8), with hand-built NSEC and
///     NSEC3 records and no signatures.
/// </summary>
public class DenialOfExistenceValidatorTests
{
    private static readonly DnsName Apex = DnsName.Parse("example.com");

    private static DnsResourceRecord BuildNsec(
        DnsName owner,
        DnsName next,
        params DnsResourceRecordTypeEnum[] types
    ) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.Nsec,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec,
                new NsecRecordData(next, types)
            )
        );

    private static DnsResourceRecord BuildNsec3(
        byte[] ownerHash,
        byte[] nextHash,
        bool isOptOut,
        params DnsResourceRecordTypeEnum[] types
    ) =>
        new(
            Nsec3Hasher.ToOwnerName(ownerHash, Apex),
            DnsResourceRecordTypeEnum.Nsec3,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec3,
                new Nsec3RecordData(1, isOptOut, 0, [], nextHash, types)
            )
        );

    private static byte[] Hash(DnsName name) => Nsec3Hasher.ComputeHash(1, name, 0, []);

    [Fact]
    public void VerifyNoDataIsTrueWhenTheNameExistsButLacksTheQueriedType()
    {
        DnsName owner = DnsName.Parse("alpha.example.com");
        DnsResourceRecord nsec = BuildNsec(
            owner,
            DnsName.Parse("omega.example.com"),
            DnsResourceRecordTypeEnum.A
        );

        Assert.True(
            DenialOfExistenceValidator.VerifyNoData(owner, DnsResourceRecordTypeEnum.Aaaa, [nsec])
        );
    }

    [Fact]
    public void VerifyNoDataIsFalseWhenTheQueriedTypeIsPresent()
    {
        DnsName owner = DnsName.Parse("alpha.example.com");
        DnsResourceRecord nsec = BuildNsec(
            owner,
            DnsName.Parse("omega.example.com"),
            DnsResourceRecordTypeEnum.A
        );

        Assert.False(
            DenialOfExistenceValidator.VerifyNoData(owner, DnsResourceRecordTypeEnum.A, [nsec])
        );
    }

    [Fact]
    public void VerifyNoDataIsFalseWithNoMatchingNsecOwner()
    {
        DnsResourceRecord nsec = BuildNsec(
            DnsName.Parse("alpha.example.com"),
            DnsName.Parse("omega.example.com"),
            DnsResourceRecordTypeEnum.A
        );

        Assert.False(
            DenialOfExistenceValidator.VerifyNoData(
                DnsName.Parse("nowhere.example.com"),
                DnsResourceRecordTypeEnum.A,
                [nsec]
            )
        );
    }

    [Fact]
    public void VerifyNxDomainProvesANameCoveredByTheNsecChainWithNoWildcardAtItsClosestEncloser()
    {
        DnsResourceRecord apexNsec = BuildNsec(
            Apex,
            DnsName.Parse("aardvark.example.com"),
            DnsResourceRecordTypeEnum.Soa
        );
        DnsResourceRecord aardvarkNsec = BuildNsec(
            DnsName.Parse("aardvark.example.com"),
            DnsName.Parse("mango.example.com"),
            DnsResourceRecordTypeEnum.A
        );
        DnsResourceRecord mangoNsec = BuildNsec(
            DnsName.Parse("mango.example.com"),
            Apex,
            DnsResourceRecordTypeEnum.A
        );

        bool result = DenialOfExistenceValidator.VerifyNxDomain(
            DnsName.Parse("banana.example.com"),
            [apexNsec, aardvarkNsec, mangoNsec]
        );

        Assert.True(result);
    }

    [Fact]
    public void VerifyNxDomainIsFalseWhenNoNsecCoversTheQueriedName()
    {
        DnsResourceRecord apexNsec = BuildNsec(
            Apex,
            DnsName.Parse("aardvark.example.com"),
            DnsResourceRecordTypeEnum.Soa
        );

        Assert.False(
            DenialOfExistenceValidator.VerifyNxDomain(
                DnsName.Parse("zebra.example.com"),
                [apexNsec]
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3IsTrueForAnExactHashMatchLackingTheQueriedType()
    {
        DnsName name = DnsName.Parse("delegated.example.com");
        DnsResourceRecord nsec3 = BuildNsec3(
            Hash(name),
            new byte[20],
            false,
            DnsResourceRecordTypeEnum.NS
        );

        Assert.True(
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                name,
                DnsResourceRecordTypeEnum.DS,
                [nsec3]
            )
        );
    }

    [Fact]
    public void VerifyNoDataNsec3IsFalseWhenTheQueriedTypeIsPresent()
    {
        DnsName name = DnsName.Parse("delegated.example.com");
        DnsResourceRecord nsec3 = BuildNsec3(
            Hash(name),
            new byte[20],
            false,
            DnsResourceRecordTypeEnum.NS
        );

        Assert.False(
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                name,
                DnsResourceRecordTypeEnum.NS,
                [nsec3]
            )
        );
    }

    [Fact]
    public void VerifyOptOutIsTrueForACoveringNsec3RangeWithTheOptOutBitSet()
    {
        DnsName delegation = DnsName.Parse("unsigned-child.example.com");
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        DnsResourceRecord nsec3 = BuildNsec3(
            allZero,
            allFf,
            isOptOut: true,
            DnsResourceRecordTypeEnum.NS
        );

        Assert.True(DenialOfExistenceValidator.VerifyOptOut(delegation, [nsec3]));
    }

    [Fact]
    public void VerifyOptOutIsFalseWhenTheCoveringRangeDoesNotHaveTheOptOutBitSet()
    {
        DnsName delegation = DnsName.Parse("unsigned-child.example.com");
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        DnsResourceRecord nsec3 = BuildNsec3(
            allZero,
            allFf,
            isOptOut: false,
            DnsResourceRecordTypeEnum.NS
        );

        Assert.False(DenialOfExistenceValidator.VerifyOptOut(delegation, [nsec3]));
    }

    [Fact]
    public void VerifyOptOutIsFalseWhenTheOptOutBitIsSetButTheRangeDoesNotActuallyCoverTheDelegation()
    {
        // The opt-out bit proves nothing unless it is on the range that covers the delegation's
        // hash. Here it is on a different range, which must not count.
        DnsName delegation = DnsName.Parse("unsigned-child.example.com");
        byte[] delegationHash = Hash(delegation);

        // A range that starts after the delegation's hash cannot cover it, wherever it ends.
        byte[] rangeStart = (byte[])delegationHash.Clone();
        rangeStart[0]++;
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();

        DnsResourceRecord nonCoveringOptOutRange = BuildNsec3(
            rangeStart,
            allFf,
            isOptOut: true,
            DnsResourceRecordTypeEnum.NS
        );

        Assert.False(DenialOfExistenceValidator.VerifyOptOut(delegation, [nonCoveringOptOutRange]));
    }

    [Fact]
    public void VerifyNxDomainNsec3ProvesAClosestEncloserANextCloserAndAnEmptyWildcard()
    {
        DnsName queried = DnsName.Parse("a.b.example.com");
        byte[] exampleComHash = Hash(Apex);
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();

        // The closest-encloser exact match.
        DnsResourceRecord exactMatch = BuildNsec3(
            exampleComHash,
            allZero,
            false,
            DnsResourceRecordTypeEnum.NS
        );
        // A catch-all range: any SHA-1 hash falls between all-zero and all-0xFF, so this one
        // entry proves both "b.example.com" and "*.example.com" absent.
        DnsResourceRecord catchAll = BuildNsec3(allZero, allFf, false, DnsResourceRecordTypeEnum.A);

        bool result = DenialOfExistenceValidator.VerifyNxDomainNsec3(
            queried,
            [exactMatch, catchAll]
        );

        Assert.True(result);
    }

    [Fact]
    public void VerifyNxDomainNsec3IsFalseWithNoClosestEncloserMatch()
    {
        DnsName queried = DnsName.Parse("a.b.example.com");
        byte[] allZero = new byte[20];
        byte[] allFf = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        DnsResourceRecord catchAll = BuildNsec3(allZero, allFf, false, DnsResourceRecordTypeEnum.A);

        Assert.False(DenialOfExistenceValidator.VerifyNxDomainNsec3(queried, [catchAll]));
    }
}

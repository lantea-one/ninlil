using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests per-hop CNAME validation in <see cref="DnssecValidator" /> (RFC 4035
///     §5.3) with signed records from <see cref="DnssecTestFixture" />: a tampered intermediate hop
///     makes the result Bogus even though the final answer verifies, and a hop in a provably
///     unsigned zone lowers an otherwise valid chain to Insecure.
/// </summary>
public sealed class DnssecValidatorCnameChainTests
{
    private readonly DnssecTestFixture _fixture = new();

    private static QueryBudget CreateBudget() => new(new RecursiveServerOptions());

    private static DnsResourceRecord BuildARecord(DnsName name) =>
        new(
            name,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse("192.0.2.1"))
            )
        );

    private static DnsResourceRecord BuildCnameRecord(DnsName owner, DnsName target) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.Cname,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Cname,
                new CnameRecordData(target)
            )
        );

    /// <summary>
    ///     This method flips one bit of an RRSIG's signature, the same tampering as
    ///     <see cref="DnssecValidatorTests.ValidateAsyncReturnsBogusWhenTheFinalAnswersSignatureWasTampered" />,
    ///     here applied to a CNAME hop.
    /// </summary>
    private static DnsResourceRecord Tamper(DnsResourceRecord genuineRrsig, DnsName owner)
    {
        RrSigRecordData data = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, genuineRrsig.Data);
        byte[] tamperedSignature = [.. data.Signature];
        tamperedSignature[0] ^= 0xFF;

        return new DnsResourceRecord(
            owner,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    data.TypeCovered,
                    data.Algorithm,
                    data.Labels,
                    data.OriginalTimeToLiveSeconds,
                    data.SignatureExpiration,
                    data.SignatureInception,
                    data.KeyTag,
                    data.SignerName,
                    tamperedSignature
                )
            )
        );
    }

    [Fact]
    public async Task ValidateAsyncReturnsBogusWhenAnIntermediateCnameHopsSignatureWasTampered()
    {
        DnssecTestFixture.ZoneKey rootKey = _fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rootDnsKeyRrsig = _fixture.Sign(
            rootKey,
            DnsName.Root,
            [rootKey.DnsKeyRecord]
        );

        DnsName exampleCom = DnsName.Parse("example.com");
        DnssecTestFixture.ZoneKey childKey = _fixture.GenerateZoneKey(exampleCom);
        DnsResourceRecord ds = _fixture.BuildDs(exampleCom, childKey);
        DnsResourceRecord dsRrsig = _fixture.Sign(rootKey, DnsName.Root, [ds]);
        DnsResourceRecord childDnsKeyRrsig = _fixture.Sign(
            childKey,
            exampleCom,
            [childKey.DnsKeyRecord]
        );

        // The CNAME hop is signed by example.com's key, but the signature carried with it is
        // corrupted, while the final answer's signature is intact.
        DnsName alias = DnsName.Parse("alias.example.com");
        DnsName www = DnsName.Parse("www.example.com");
        DnsResourceRecord cnameRecord = BuildCnameRecord(alias, www);
        DnsResourceRecord genuineCnameRrsig = _fixture.Sign(childKey, exampleCom, [cnameRecord]);
        DnsResourceRecord tamperedCnameRrsig = Tamper(genuineCnameRrsig, alias);

        DnsResourceRecord answer = BuildARecord(www);
        DnsResourceRecord answerRrsig = _fixture.Sign(childKey, exampleCom, [answer]);

        FakeIterativeResolutionEngine engine = new();
        engine.AddResult(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [rootKey.DnsKeyRecord],
                [rootDnsKeyRrsig],
                null,
                [DnsName.Root]
            )
        );
        engine.AddResult(
            exampleCom,
            DnsResourceRecordTypeEnum.DS,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [ds],
                [dsRrsig],
                null,
                [DnsName.Root]
            )
        );
        engine.AddResult(
            exampleCom,
            DnsResourceRecordTypeEnum.DnsKey,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [childKey.DnsKeyRecord],
                [childDnsKeyRrsig],
                null,
                [exampleCom]
            )
        );

        DnssecValidator validator = new(
            engine,
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)]),
            ValidatorTestHelpers.CreateTrustCache()
        );

        IterativeResolutionResult result = new(
            RecursiveResolutionStatusEnum.Success,
            [cnameRecord, answer],
            [answerRrsig],
            null,
            [exampleCom]
        )
        {
            CnameChain = [new CnameChainHop(cnameRecord, [tamperedCnameRrsig], exampleCom)],
        };

        DnssecValidationResult validation = await validator.ValidateAsync(
            alias,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            result,
            CreateBudget()
        );

        // Bogus, not Secure: the hop's signature is checked on its own, although the final answer
        // verifies.
        Assert.Equal(DnssecValidationStateEnum.Bogus, validation.State);
    }

    [Fact]
    public async Task ValidateAsyncDowngradesToInsecureWhenAnIntermediateCnameHopLandsInAProvablyUnsignedZone()
    {
        DnssecTestFixture.ZoneKey rootKey = _fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rootDnsKeyRrsig = _fixture.Sign(
            rootKey,
            DnsName.Root,
            [rootKey.DnsKeyRecord]
        );

        // insecure.com: a provably unsigned delegation (NSEC3 proves no DS), as in
        // DnssecValidatorTests.ValidateAsyncReturnsInsecureForAProvablyUnsignedDelegation.
        DnsName insecureCom = DnsName.Parse("insecure.com");
        byte[] hash = Nsec3Hasher.ComputeHash(1, insecureCom, 0, []);
        DnsResourceRecord nsec3 = new(
            Nsec3Hasher.ToOwnerName(hash, DnsName.Root),
            DnsResourceRecordTypeEnum.Nsec3,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec3,
                new Nsec3RecordData(1, false, 0, [], new byte[20], [DnsResourceRecordTypeEnum.NS])
            )
        );
        DnsResourceRecord nsec3Rrsig = _fixture.Sign(rootKey, DnsName.Root, [nsec3]);

        // example.com: fully signed, the final answer's zone.
        DnsName exampleCom = DnsName.Parse("example.com");
        DnssecTestFixture.ZoneKey childKey = _fixture.GenerateZoneKey(exampleCom);
        DnsResourceRecord ds = _fixture.BuildDs(exampleCom, childKey);
        DnsResourceRecord dsRrsig = _fixture.Sign(rootKey, DnsName.Root, [ds]);
        DnsResourceRecord childDnsKeyRrsig = _fixture.Sign(
            childKey,
            exampleCom,
            [childKey.DnsKeyRecord]
        );

        DnsName alias = DnsName.Parse("alias.insecure.com");
        DnsName www = DnsName.Parse("www.example.com");
        // A hop in an unsigned zone carries no RRSIG (see CnameChainHop).
        DnsResourceRecord cnameRecord = BuildCnameRecord(alias, www);

        DnsResourceRecord answer = BuildARecord(www);
        DnsResourceRecord answerRrsig = _fixture.Sign(childKey, exampleCom, [answer]);

        FakeIterativeResolutionEngine engine = new();
        engine.AddResult(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [rootKey.DnsKeyRecord],
                [rootDnsKeyRrsig],
                null,
                [DnsName.Root]
            )
        );
        engine.AddResult(
            insecureCom,
            DnsResourceRecordTypeEnum.DS,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.NoData,
                [],
                [nsec3, nsec3Rrsig],
                null,
                [DnsName.Root]
            )
        );
        engine.AddResult(
            exampleCom,
            DnsResourceRecordTypeEnum.DS,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [ds],
                [dsRrsig],
                null,
                [DnsName.Root]
            )
        );
        engine.AddResult(
            exampleCom,
            DnsResourceRecordTypeEnum.DnsKey,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [childKey.DnsKeyRecord],
                [childDnsKeyRrsig],
                null,
                [exampleCom]
            )
        );

        DnssecValidator validator = new(
            engine,
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)]),
            ValidatorTestHelpers.CreateTrustCache()
        );

        IterativeResolutionResult result = new(
            RecursiveResolutionStatusEnum.Success,
            [cnameRecord, answer],
            [answerRrsig],
            null,
            [exampleCom]
        )
        {
            CnameChain = [new CnameChainHop(cnameRecord, [], insecureCom)],
        };

        DnssecValidationResult validation = await validator.ValidateAsync(
            alias,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            result,
            CreateBudget()
        );

        // Insecure, not Secure, although the final answer's zone validates: one unsigned hop caps
        // the whole result.
        Assert.Equal(DnssecValidationStateEnum.Insecure, validation.State);
    }
}

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
///     This class covers RFC 4035 §5.3's per-CNAME-hop chain-of-trust validation --
///     <see cref="DnssecValidator" />'s own real behavior against a genuine, multi-record
///     <see cref="IterativeResolutionResult.CnameChain" />, previously entirely untested (every
///     prior test in <see cref="DnssecValidatorTests" /> exercises a chain with zero CNAME hops).
///     Confirms a hop's own signature is authenticated independently of the terminal answer -- a
///     tampered intermediate hop fails the whole validation even though the terminal answer's own
///     signature is genuinely valid, and a hop landing in a real, provably-unsigned delegation
///     correctly downgrades an otherwise-fully-valid chain from Secure to Insecure rather than
///     silently ignoring it.
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
    ///     Flips one bit of a genuine RRSIG's own signature -- the same tampering technique
    ///     <see cref="DnssecValidatorTests.ValidateAsyncReturnsBogusWhenTheFinalAnswersSignatureWasTampered" />
    ///     already established, applied here to a CNAME hop's own signature instead of the
    ///     terminal answer's.
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

        // The CNAME hop itself is genuinely signed by example.com's own real key -- but the
        // signature actually carried alongside it (as a real resolution would cache/forward) is
        // corrupted, simulating an attacker (or a genuine transmission fault) tampering with an
        // intermediate hop specifically, while leaving the terminal answer's own signature intact.
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
            new EmptyRecursiveCache(),
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)])
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

        // Bogus -- not Secure -- proving this hop's own signature is genuinely checked
        // independently of the terminal answer's (which, on its own, validates perfectly fine).
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

        // insecure.com: a real, provably-unsigned delegation (NSEC3 proves no DS exists), the
        // exact same shape DnssecValidatorTests.ValidateAsyncReturnsInsecureForAProvablyUnsignedDelegation
        // already establishes -- this hop's own zone genuinely has no DNSSEC material at all.
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

        // example.com: fully signed, genuinely valid -- the terminal answer's own zone.
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
        // A hop genuinely owned by an unsigned zone carries no RRSIG at all -- there is nothing
        // to verify against, per CnameChainHop's own doc comment.
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
            new EmptyRecursiveCache(),
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)])
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

        // Insecure -- not Secure -- even though the terminal answer's own zone validates
        // perfectly: one genuinely unsigned hop anywhere in the chain caps the whole result.
        Assert.Equal(DnssecValidationStateEnum.Insecure, validation.State);
    }
}

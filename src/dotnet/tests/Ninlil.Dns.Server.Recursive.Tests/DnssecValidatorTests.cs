using Microsoft.Extensions.Options;
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
///     This class tests <see cref="DnssecValidator" /> end to end with signed DNSKEY, DS and
///     RRSIG records built by <see cref="DnssecTestFixture" />, and a fake
///     <see cref="IIterativeResolutionEngine" /> in place of the network so each zone cut's DS
///     and DNSKEY answers are controlled.
/// </summary>
public class DnssecValidatorTests
{
    private readonly DnssecTestFixture _fixture = new();

    private static QueryBudget CreateBudget() => new(new RecursiveServerOptions());

    [Fact]
    public async Task ValidateAsyncReturnsSecureForAFullyValidTwoLevelChain()
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

        DnsName www = DnsName.Parse("www.example.com");
        DnsResourceRecord answer = new(
            www,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(System.Net.IPAddress.Parse("192.0.2.1"))
            )
        );
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
            [answer],
            [answerRrsig],
            null,
            [exampleCom]
        );

        DnssecValidationResult validation = await validator.ValidateAsync(
            www,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            result,
            CreateBudget()
        );

        Assert.Equal(DnssecValidationStateEnum.Secure, validation.State);
    }

    [Fact]
    public async Task ValidateAsyncReturnsBogusWhenTheFinalAnswersSignatureWasTampered()
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

        DnsName www = DnsName.Parse("www.example.com");
        DnsResourceRecord answer = new(
            www,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(System.Net.IPAddress.Parse("192.0.2.1"))
            )
        );
        DnsResourceRecord genuineRrsig = _fixture.Sign(childKey, exampleCom, [answer]);
        RrSigRecordData tamperedData = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, genuineRrsig.Data);
        byte[] tamperedSignature = [.. tamperedData.Signature];
        tamperedSignature[0] ^= 0xFF;
        DnsResourceRecord tamperedRrsig = new(
            www,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    tamperedData.TypeCovered,
                    tamperedData.Algorithm,
                    tamperedData.Labels,
                    tamperedData.OriginalTimeToLiveSeconds,
                    tamperedData.SignatureExpiration,
                    tamperedData.SignatureInception,
                    tamperedData.KeyTag,
                    tamperedData.SignerName,
                    tamperedSignature
                )
            )
        );

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
            [answer],
            [tamperedRrsig],
            null,
            [exampleCom]
        );

        DnssecValidationResult validation = await validator.ValidateAsync(
            www,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            result,
            CreateBudget()
        );

        Assert.Equal(DnssecValidationStateEnum.Bogus, validation.State);
    }

    [Fact]
    public async Task ValidateAsyncReturnsInsecureForAProvablyUnsignedDelegation()
    {
        DnssecTestFixture.ZoneKey rootKey = _fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rootDnsKeyRrsig = _fixture.Sign(
            rootKey,
            DnsName.Root,
            [rootKey.DnsKeyRecord]
        );

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

        DnssecValidator validator = new(
            engine,
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)]),
            ValidatorTestHelpers.CreateTrustCache()
        );

        IterativeResolutionResult result = new(
            RecursiveResolutionStatusEnum.NoData,
            [],
            [],
            null,
            [insecureCom]
        );

        DnssecValidationResult validation = await validator.ValidateAsync(
            DnsName.Parse("www.insecure.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            result,
            CreateBudget()
        );

        Assert.Equal(DnssecValidationStateEnum.Insecure, validation.State);
    }

    [Fact]
    public async Task ValidateAsyncReturnsBogusWhenTheRootDnsKeyCannotBeResolvedUnderATrustAnchor()
    {
        FakeIterativeResolutionEngine engine = new();
        DnssecTestFixture.ZoneKey rootKey = _fixture.GenerateZoneKey(DnsName.Root);

        DnssecValidator validator = new(
            engine,
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)]),
            ValidatorTestHelpers.CreateTrustCache()
        );

        IterativeResolutionResult result = new(
            RecursiveResolutionStatusEnum.Success,
            [],
            [],
            null,
            [DnsName.Root]
        );

        DnssecValidationResult validation = await validator.ValidateAsync(
            DnsName.Parse("www.example.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            result,
            CreateBudget()
        );

        // Missing data that the chain of trust needs is Bogus (RFC 4035 §4.3); only the lack of a
        // trust anchor is Indeterminate.
        Assert.Equal(DnssecValidationStateEnum.Bogus, validation.State);
    }

    [Fact]
    public async Task ValidateAsyncReturnsIndeterminateWhenNoTrustAnchorIsUsable()
    {
        DnssecValidator validator = new(
            new FakeIterativeResolutionEngine(),
            new FakeTrustAnchorStore([]),
            ValidatorTestHelpers.CreateTrustCache()
        );

        DnssecValidationResult validation = await validator.ValidateAsync(
            DnsName.Parse("www.example.com"),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [],
                [],
                null,
                [DnsName.Root]
            ),
            CreateBudget()
        );

        Assert.Equal(DnssecValidationStateEnum.Indeterminate, validation.State);
    }

    [Fact]
    public async Task ValidateAsyncReturnsInsecureWhenTheSignedDsSetHasNoSupportedDigest()
    {
        DnssecTestFixture.ZoneKey rootKey = _fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rootDnsKeyRrsig = _fixture.Sign(
            rootKey,
            DnsName.Root,
            [rootKey.DnsKeyRecord]
        );

        DnsName exampleCom = DnsName.Parse("example.com");
        DnssecTestFixture.ZoneKey childKey = _fixture.GenerateZoneKey(exampleCom);

        // A DS with digest type 3 (GOST), which this validator cannot compute (RFC 4035 §5.2).
        DnsResourceRecord ds = new(
            exampleCom,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.DS,
                new DsRecordData(childKey.KeyTag, DnssecAlgorithmEnum.Ed25519, 3, [1, 2, 3, 4])
            )
        );
        DnsResourceRecord dsRrsig = _fixture.Sign(rootKey, DnsName.Root, [ds]);

        DnsName www = DnsName.Parse("www.example.com");
        DnsResourceRecord answer = new(
            www,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(System.Net.IPAddress.Parse("192.0.2.1"))
            )
        );

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

        DnssecValidator validator = new(
            engine,
            new FakeTrustAnchorStore([_fixture.BuildTrustAnchor(DnsName.Root, rootKey)]),
            ValidatorTestHelpers.CreateTrustCache()
        );

        DnssecValidationResult validation = await validator.ValidateAsync(
            www,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [answer],
                [],
                null,
                [exampleCom]
            ),
            CreateBudget()
        );

        Assert.Equal(DnssecValidationStateEnum.Insecure, validation.State);
    }
}

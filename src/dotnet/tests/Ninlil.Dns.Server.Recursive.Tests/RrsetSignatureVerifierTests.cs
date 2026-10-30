using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests the RFC 4035 §5.3.1 checks of <see cref="RrsetSignatureVerifier.Verify" />
///     (signer name, Labels, Zone Key bit; RFC 5011 §2.1 for the REVOKE bit), the work budget
///     and the DS handling of
///     <see cref="RrsetSignatureVerifier.FindKeysMatchingDs" /> and
///     <see cref="RrsetSignatureVerifier.HasSupportedDs" /> (unsupported digests and algorithms,
///     RFC 4509 §3). It uses real Ed25519 keys from <see cref="DnssecTestFixture" />, so every
///     accepted signature is a genuine one.
/// </summary>
public class RrsetSignatureVerifierTests
{
    private static readonly DnsName Apex = DnsName.Parse("example.com");
    private static readonly DnsName Owner = DnsName.Parse("www.example.com");
    private readonly DnssecTestFixture fixture = new();

    private static DnsResourceRecord ARecord(DnsName owner) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse("192.0.2.1"))
            )
        );

    /// <summary>
    ///     This method re-encodes an RRSIG record with a different Labels field, keeping the
    ///     signature bytes.
    /// </summary>
    private static DnsResourceRecord WithLabels(DnsResourceRecord rrsig, byte labels)
    {
        RrSigRecordData data = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, rrsig.Data);

        return new DnsResourceRecord(
            rrsig.Name,
            rrsig.Type,
            rrsig.Class,
            rrsig.TimeToLive,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    data.TypeCovered,
                    data.Algorithm,
                    labels,
                    data.OriginalTimeToLiveSeconds,
                    data.SignatureExpiration,
                    data.SignatureInception,
                    data.KeyTag,
                    data.SignerName,
                    data.Signature
                )
            )
        );
    }

    /// <summary>
    ///     This method re-encodes an RRSIG record with a different signature.
    /// </summary>
    private static DnsResourceRecord WithSignature(DnsResourceRecord rrsig, byte[] signature)
    {
        RrSigRecordData data = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, rrsig.Data);

        return new DnsResourceRecord(
            rrsig.Name,
            rrsig.Type,
            rrsig.Class,
            rrsig.TimeToLive,
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
                    signature
                )
            )
        );
    }

    /// <summary>
    ///     This method builds a zone key with chosen flags over the fixture key pair's public key,
    ///     so the key pair can still sign for it.
    /// </summary>
    private DnssecTestFixture.ZoneKey KeyWithFlags(
        DnssecTestFixture.ZoneKey source,
        bool isZoneKey,
        bool isRevoked
    )
    {
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(
                isZoneKey,
                false,
                DnssecAlgorithmEnum.Ed25519,
                source.KeyPair.PublicKeyWireFormat,
                isRevoked
            )
        );

        return new DnssecTestFixture.ZoneKey(
            source.KeyPair,
            new DnsResourceRecord(
                source.DnsKeyRecord.Name,
                DnsResourceRecordTypeEnum.DnsKey,
                DnsClassEnum.Internet,
                3600,
                rdata
            ),
            DnssecKeyTag.Compute(DnssecAlgorithmEnum.Ed25519, rdata)
        );
    }

    [Fact]
    public void Verify_ForAnRrsetSignedByTheZoneKey_Succeeds()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.True(result.IsVerified);
        Assert.NotNull(result.VerifyingSignature);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Verify_ForAKeyOwnedByAnotherNameThanTheSigner_IsNotVerified()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(DnsName.Parse("other.com"));
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_ForASignerThatIsNotTheZone_IsNotVerified()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            DnsName.Parse("com"),
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_ForASignerThatIsNotAnAncestorOfTheOwner_IsNotVerified()
    {
        // The signature and key are genuine, but example.com is not above www.example.org.
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);
        List<DnsResourceRecord> rrset = [ARecord(DnsName.Parse("www.example.org"))];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_ForALabelsFieldAboveTheOwnerLabelCount_IsNotVerified()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = WithLabels(fixture.Sign(key, Apex, rrset), 4);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_ForAKeyWithoutTheZoneKeyBit_IsNotVerified()
    {
        DnssecTestFixture.ZoneKey key = KeyWithFlags(
            fixture.GenerateZoneKey(Apex),
            isZoneKey: false,
            isRevoked: false
        );
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_ForARevokedKey_IsNotVerified()
    {
        DnssecTestFixture.ZoneKey key = KeyWithFlags(
            fixture.GenerateZoneKey(Apex),
            isZoneKey: true,
            isRevoked: true
        );
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            new SignatureCheckBudget()
        );

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_For20SignaturesAnd20KeysWithTheSameTagThatAllFail_StopsAfterEightChecks()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord valid = fixture.Sign(key, Apex, rrset);
        // The same key twenty times gives twenty keys with one tag, which is what a KeyTrap
        // response relies on. Each RRSIG has its own wrong 64-byte signature.
        List<DnsResourceRecord> keys = [.. Enumerable.Repeat(key.DnsKeyRecord, 20)];
        List<DnsResourceRecord> signatures =
        [
            .. Enumerable
                .Range(1, 20)
                .Select(i => WithSignature(valid, [.. Enumerable.Repeat((byte)i, 64)])),
        ];
        SignatureCheckBudget budget = new();

        RrsetVerificationResult result = RrsetSignatureVerifier.Verify(
            rrset,
            signatures,
            keys,
            Apex,
            DateTimeOffset.UtcNow,
            budget
        );

        Assert.False(result.IsVerified);
        Assert.Equal(8, budget.TotalUsed);
        Assert.Contains(
            "signature work limit",
            result.FailureReason,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Verify_AfterTheValidationLimitIsSpent_FailsEvenForAValidRrset()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);
        List<DnsResourceRecord> rrset = [ARecord(Owner)];
        DnsResourceRecord rrsig = fixture.Sign(key, Apex, rrset);
        SignatureCheckBudget budget = new(maxPerRrset: 8, maxPerValidation: 2);

        for (int i = 0; i < 2; i++)
            Assert.True(
                RrsetSignatureVerifier
                    .Verify(rrset, [rrsig], [key.DnsKeyRecord], Apex, DateTimeOffset.UtcNow, budget)
                    .IsVerified
            );

        RrsetVerificationResult third = RrsetSignatureVerifier.Verify(
            rrset,
            [rrsig],
            [key.DnsKeyRecord],
            Apex,
            DateTimeOffset.UtcNow,
            budget
        );

        Assert.False(third.IsVerified);
    }

    private DnsResourceRecord Ds(
        DnssecTestFixture.ZoneKey key,
        byte digestType,
        bool wrongDigest = false,
        DnssecAlgorithmEnum algorithm = DnssecAlgorithmEnum.Ed25519
    )
    {
        byte[] digest = DigestFor(key, digestType);

        if (wrongDigest)
            digest[0] ^= 0xFF;

        return new DnsResourceRecord(
            Apex,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.DS,
                new DsRecordData(key.KeyTag, algorithm, digestType, digest)
            )
        );
    }

    private static byte[] DigestFor(DnssecTestFixture.ZoneKey key, byte digestType) =>
        DsDigestCodec.IsSupported(digestType)
            ? DsDigestCodec.Compute(digestType, Apex, key.DnsKeyRecord.Data)
            : [1, 2, 3, 4];

    [Fact]
    public void FindKeysMatchingDs_ForADsSetWithTypes2And3_MatchesViaType2WithoutThrowing()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);

        IReadOnlyList<DnsResourceRecord> matches = RrsetSignatureVerifier.FindKeysMatchingDs(
            [key.DnsKeyRecord],
            Apex,
            [Ds(key, 3), Ds(key, 2)]
        );

        Assert.Single(matches);
    }

    [Fact]
    public void HasSupportedDs_ForAGostDsOnly_IsFalse()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);

        Assert.False(RrsetSignatureVerifier.HasSupportedDs([Ds(key, 3)]));
        Assert.True(RrsetSignatureVerifier.HasSupportedDs([Ds(key, 3), Ds(key, 2)]));
    }

    [Fact]
    public void HasSupportedDs_ForAnUnsupportedAlgorithmOnly_IsFalse()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);

        Assert.False(
            RrsetSignatureVerifier.HasSupportedDs([
                Ds(key, 2, algorithm: DnssecAlgorithmEnum.EccGost),
            ])
        );
    }

    [Fact]
    public void FindKeysMatchingDs_ForAWrongSha1AndACorrectSha256_Matches()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);

        IReadOnlyList<DnsResourceRecord> matches = RrsetSignatureVerifier.FindKeysMatchingDs(
            [key.DnsKeyRecord],
            Apex,
            [Ds(key, 1, wrongDigest: true), Ds(key, 2)]
        );

        Assert.Single(matches);
    }

    [Fact]
    public void FindKeysMatchingDs_ForACorrectSha1AndAWrongSha256_DoesNotMatch()
    {
        // RFC 4509 §3: with a SHA-256 DS present, the SHA-1 DS is ignored.
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);

        IReadOnlyList<DnsResourceRecord> matches = RrsetSignatureVerifier.FindKeysMatchingDs(
            [key.DnsKeyRecord],
            Apex,
            [Ds(key, 1), Ds(key, 2, wrongDigest: true)]
        );

        Assert.Empty(matches);
    }

    [Fact]
    public void FindKeysMatchingDs_ForACorrectSha1Alone_Matches()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(Apex);

        IReadOnlyList<DnsResourceRecord> matches = RrsetSignatureVerifier.FindKeysMatchingDs(
            [key.DnsKeyRecord],
            Apex,
            [Ds(key, 1)]
        );

        Assert.Single(matches);
    }

    [Fact]
    public void FindKeysMatchingTrustAnchor_ForAZoneKeyWithTheAnchorsDigest_ReturnsIt()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(DnsName.Root);

        IReadOnlyList<DnsResourceRecord> matches =
            RrsetSignatureVerifier.FindKeysMatchingTrustAnchor(
                [key.DnsKeyRecord],
                DnsName.Root,
                [fixture.BuildTrustAnchor(DnsName.Root, key)]
            );

        Assert.Single(matches);
    }

    [Fact]
    public void FindKeysMatchingTrustAnchor_ForAKeyWithoutTheZoneKeyBit_ReturnsNothing()
    {
        DnssecTestFixture.ZoneKey key = KeyWithFlags(
            fixture.GenerateZoneKey(DnsName.Root),
            isZoneKey: false,
            isRevoked: false
        );

        IReadOnlyList<DnsResourceRecord> matches =
            RrsetSignatureVerifier.FindKeysMatchingTrustAnchor(
                [key.DnsKeyRecord],
                DnsName.Root,
                [fixture.BuildTrustAnchor(DnsName.Root, key)]
            );

        Assert.Empty(matches);
    }

    [Fact]
    public void FindKeysMatchingTrustAnchor_ForARevokedKey_ReturnsNothing()
    {
        DnssecTestFixture.ZoneKey key = KeyWithFlags(
            fixture.GenerateZoneKey(DnsName.Root),
            isZoneKey: true,
            isRevoked: true
        );

        IReadOnlyList<DnsResourceRecord> matches =
            RrsetSignatureVerifier.FindKeysMatchingTrustAnchor(
                [key.DnsKeyRecord],
                DnsName.Root,
                [fixture.BuildTrustAnchor(DnsName.Root, key)]
            );

        Assert.Empty(matches);
    }

    [Fact]
    public void FindKeysMatchingTrustAnchor_ForAnAnchorWithAnUnsupportedDigestType_ReturnsNothingAndDoesNotThrow()
    {
        DnssecTestFixture.ZoneKey key = fixture.GenerateZoneKey(DnsName.Root);
        TrustAnchor gost = fixture.BuildTrustAnchor(DnsName.Root, key) with { DigestType = 3 };

        IReadOnlyList<DnsResourceRecord> matches =
            RrsetSignatureVerifier.FindKeysMatchingTrustAnchor(
                [key.DnsKeyRecord],
                DnsName.Root,
                [gost]
            );

        Assert.Empty(matches);
    }
}

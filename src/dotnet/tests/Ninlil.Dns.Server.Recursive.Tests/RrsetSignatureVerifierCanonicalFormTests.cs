using System.Buffers.Binary;
using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests that <see cref="RrsetSignatureVerifier.TryVerify" /> follows the canonical
///     form rules that decide whether a signature made elsewhere verifies: NSEC Next Domain Names
///     keep their case and the RRSIG Signer's Name is lower-cased (RFC 6840 §5.1, which
///     corrects RFC 4034 §6.2), and a wildcard expansion is verified against the wildcard owner
///     (RFC 4035 §5.3.2). The signing input is assembled byte by byte in the test with an
///     Ed25519 key, so it does not depend on
///     <see cref="DnssecCanonicalization.BuildSigningInput" />. A second group replays
///     dnspython 2.8.0 vectors from <c>Vectors/dnssec-canonical-vectors.json</c>.
/// </summary>
public class RrsetSignatureVerifierCanonicalFormTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1750000000);

    [Fact]
    public void TryVerify_ForAnNsecWithAMixedCaseNextName_VerifiesWhenSignedOverTheOriginalCase()
    {
        SignedSet set = Sign(
            "a.example",
            DnsResourceRecordTypeEnum.Nsec,
            NsecRdata("B.Example."),
            "example",
            "a.example",
            2,
            lowerCaseNextName: false
        );

        Assert.True(RrsetSignatureVerifier.TryVerify(set.Rrset, [set.Rrsig], [set.Key], Now));
    }

    [Fact]
    public void TryVerify_ForAnNsecSignedOverALowerCasedNextName_IsRejected()
    {
        // The case Ninlil used to produce: a conformant validator rejects it, so Ninlil must too.
        SignedSet set = Sign(
            "a.example",
            DnsResourceRecordTypeEnum.Nsec,
            NsecRdata("B.Example."),
            "example",
            "a.example",
            2,
            lowerCaseNextName: true
        );

        Assert.False(RrsetSignatureVerifier.TryVerify(set.Rrset, [set.Rrsig], [set.Key], Now));
    }

    [Fact]
    public void BuildSigningInput_ForAnNsecWithAMixedCaseNextName_ContainsTheNextNameInThatCase()
    {
        SignedSet set = Sign(
            "a.example",
            DnsResourceRecordTypeEnum.Nsec,
            NsecRdata("B.Example."),
            "example",
            "a.example",
            2,
            lowerCaseNextName: false
        );
        RrSigRecordData rrsig = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, set.Rrsig.Data);

        byte[] input = DnssecCanonicalization.BuildSigningInput(
            rrsig,
            set.Rrset[0].Name,
            DnsClassEnum.Internet,
            set.Rrset
        );

        // Length-prefixed labels "B" and "Example", then the root.
        byte[] mixedCase = [1, (byte)'B', 7, .. "Example"u8, 0];
        Assert.True(input.AsSpan().IndexOf(mixedCase) >= 0);
    }

    [Fact]
    public void TryVerify_ForAnRrsigWithAMixedCaseSignerName_VerifiesWhenSignedOverTheLowerCaseSigner()
    {
        SignedSet set = Sign(
            "www.example.com",
            DnsResourceRecordTypeEnum.A,
            [192, 0, 2, 1],
            "Example.COM.",
            "www.example.com",
            3
        );

        Assert.True(RrsetSignatureVerifier.TryVerify(set.Rrset, [set.Rrsig], [set.Key], Now));
    }

    [Fact]
    public void TryVerify_ForAWildcardExpansion_VerifiesAgainstTheWildcardOwner()
    {
        // Signed at *.example.com (Labels 2), presented as x.y.example.com.
        SignedSet set = Sign(
            "x.y.example.com",
            DnsResourceRecordTypeEnum.A,
            [192, 0, 2, 7],
            "example.com",
            "*.example.com",
            2
        );

        Assert.True(RrsetSignatureVerifier.TryVerify(set.Rrset, [set.Rrsig], [set.Key], Now));
    }

    [Fact]
    public void TryVerify_ForALabelsValueLargerThanTheOwner_ReturnsFalseWithoutThrowing()
    {
        SignedSet set = Sign(
            "example.com",
            DnsResourceRecordTypeEnum.A,
            [192, 0, 2, 7],
            "example.com",
            "example.com",
            5
        );

        Assert.False(RrsetSignatureVerifier.TryVerify(set.Rrset, [set.Rrsig], [set.Key], Now));
    }

    /// <summary>
    ///     This test replays the dnspython 2.8.0 vectors: each RRset and its RRSIG were produced by
    ///     dnspython, so a pass shows Ninlil builds the same signing input as an independent
    ///     implementation.
    /// </summary>
    [Theory]
    [InlineData("nsec-mixed-case-next", "example.")]
    [InlineData("signer-mixed-case", "example.com.")]
    [InlineData("wildcard-expansion", "example.com.")]
    public void TryVerify_ForADnspythonVector_Verifies(string id, string apex)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Vectors", "dnssec-canonical-vectors.json")
            )
        );
        JsonElement vector = document
            .RootElement.EnumerateArray()
            .Single(element => element.GetProperty("id").GetString() == id);

        DnsName owner = DnsName.Parse(vector.GetProperty("ownerName").GetString());
        DnsResourceRecordTypeEnum type = Enum.Parse<DnsResourceRecordTypeEnum>(
            vector.GetProperty("type").GetString(),
            ignoreCase: true
        );
        List<DnsResourceRecord> rrset =
        [
            .. vector
                .GetProperty("rdataHex")
                .EnumerateArray()
                .Select(hex => new DnsResourceRecord(
                    owner,
                    type,
                    DnsClassEnum.Internet,
                    3600,
                    Convert.FromHexString(hex.GetString())
                )),
        ];
        DnsResourceRecord rrsig = new(
            owner,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            3600,
            Convert.FromHexString(vector.GetProperty("rrsigHex").GetString())
        );
        DnsResourceRecord key = new(
            DnsName.Parse(apex),
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            3600,
            Convert.FromHexString(vector.GetProperty("dnskeyHex").GetString())
        );
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(
            vector.GetProperty("now").GetInt64()
        );

        Assert.True(RrsetSignatureVerifier.TryVerify(rrset, [rrsig], [key], now));
    }

    /// <summary>
    ///     This record carries a signed RRset, its RRSIG and the DNSKEY that verifies it.
    /// </summary>
    private sealed record SignedSet(
        List<DnsResourceRecord> Rrset,
        DnsResourceRecord Rrsig,
        DnsResourceRecord Key
    );

    /// <summary>
    ///     This method signs one single-record RRset with a fresh Ed25519 key over a signing input
    ///     assembled by hand from RFC 4034 §3.1.8.1.
    /// </summary>
    /// <param name="presentedOwner">The owner the RRset is presented under.</param>
    /// <param name="type">The RRset's type.</param>
    /// <param name="rdata">The record's RDATA.</param>
    /// <param name="signer">The Signer's Name written into the RRSIG, in the given case.</param>
    /// <param name="signedOwner">The owner the signature is computed over.</param>
    /// <param name="labels">The RRSIG Labels field.</param>
    /// <param name="lowerCaseNextName">
    ///     For an NSEC, whether the signature is computed over a lower-cased Next Domain Name (the
    ///     wrong form).
    /// </param>
    private static SignedSet Sign(
        string presentedOwner,
        DnsResourceRecordTypeEnum type,
        byte[] rdata,
        string signer,
        string signedOwner,
        byte labels,
        bool lowerCaseNextName = false
    )
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Ed25519,
            out IDnssecSigningAlgorithm algorithm
        );
        DnssecKeyPair pair = algorithm.GenerateKeyPair();
        byte[] keyRdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(true, false, DnssecAlgorithmEnum.Ed25519, pair.PublicKeyWireFormat)
        );
        ushort keyTag = DnssecKeyTag.Compute(DnssecAlgorithmEnum.Ed25519, keyRdata);
        DnsName signerName = DnsName.Parse(signer);
        uint inception = (uint)Now.AddDays(-1).ToUnixTimeSeconds();
        uint expiration = (uint)Now.AddDays(1).ToUnixTimeSeconds();

        // RRSIG RDATA without the signature, with the signer in lower case as the RFC requires.
        List<byte> input = [];
        input.AddRange(
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    type,
                    DnssecAlgorithmEnum.Ed25519,
                    labels,
                    3600,
                    expiration,
                    inception,
                    keyTag,
                    signerName.ToCanonicalLower(),
                    []
                )
            )
        );

        // One record: lower-case owner, type, class, original TTL, RDLENGTH, RDATA.
        byte[] signedRdata = lowerCaseNextName ? LowerCaseAscii(rdata) : rdata;
        input.AddRange(DnsName.Parse(signedOwner).ToCanonicalLower().ToUncompressedWireBytes());
        Span<byte> header = stackalloc byte[10];
        BinaryPrimitives.WriteUInt16BigEndian(header[..2], (ushort)type);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), 3600);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(8, 2), (ushort)signedRdata.Length);
        input.AddRange(header.ToArray());
        input.AddRange(signedRdata);

        byte[] signature = algorithm.Sign(input.ToArray(), pair.PrivateKeyPkcs8);

        DnsName owner = DnsName.Parse(presentedOwner);
        DnsResourceRecord record = new(owner, type, DnsClassEnum.Internet, 3600, rdata);
        DnsResourceRecord rrsig = new(
            owner,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    type,
                    DnssecAlgorithmEnum.Ed25519,
                    labels,
                    3600,
                    expiration,
                    inception,
                    keyTag,
                    signerName,
                    signature
                )
            )
        );
        DnsResourceRecord key = new(
            signerName,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            3600,
            keyRdata
        );

        return new SignedSet([record], rrsig, key);
    }

    /// <summary>
    ///     This method builds NSEC RDATA with the given Next Domain Name and an A, RRSIG, NSEC type
    ///     bitmap.
    /// </summary>
    private static byte[] NsecRdata(string nextName) =>
        [.. DnsName.Parse(nextName).ToUncompressedWireBytes(), 0, 6, 0x40, 0, 0, 0, 0, 3];

    /// <summary>
    ///     This method lower-cases every ASCII letter in a byte array. The bitmap bytes of
    ///     <see cref="NsecRdata" /> are not letters, so only the name changes.
    /// </summary>
    private static byte[] LowerCaseAscii(byte[] bytes) =>
        [.. bytes.Select(b => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b)];
}

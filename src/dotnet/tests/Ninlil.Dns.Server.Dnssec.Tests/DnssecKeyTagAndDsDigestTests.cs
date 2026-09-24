using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class tests <see cref="DnssecKeyTag" /> (RFC 4034 Appendix B) and
///     <see cref="DsDigestCodec" /> (RFC 4034 §5.1.4).
/// </summary>
public class DnssecKeyTagAndDsDigestTests
{
    private static byte[] BuildDnsKeyRdata(
        DnssecAlgorithmEnum algorithm,
        byte[] publicKeyWireFormat
    )
    {
        byte[] rdata = new byte[4 + publicKeyWireFormat.Length];
        rdata[0] = 0x01; // Flags high byte: bit 7 (ZoneKey) set.
        rdata[2] = 3; // Protocol.
        rdata[3] = (byte)algorithm;
        publicKeyWireFormat.CopyTo(rdata, 4);
        return rdata;
    }

    [Fact]
    public void KeyTagComputationIsDeterministic()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha256,
            out IDnssecSigningAlgorithm rsaSha256
        );
        byte[] dnsKeyRdata = BuildDnsKeyRdata(
            DnssecAlgorithmEnum.RsaSha256,
            rsaSha256.GenerateKeyPair(1024).PublicKeyWireFormat
        );

        ushort first = DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha256, dnsKeyRdata);
        ushort second = DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha256, dnsKeyRdata);

        Assert.Equal(first, second);
    }

    [Fact]
    public void KeyTagChangesWhenTheRdataChanges()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha256,
            out IDnssecSigningAlgorithm rsaSha256
        );
        byte[] dnsKeyRdata = BuildDnsKeyRdata(
            DnssecAlgorithmEnum.RsaSha256,
            rsaSha256.GenerateKeyPair(1024).PublicKeyWireFormat
        );
        byte[] tamperedRdata = [.. dnsKeyRdata];
        tamperedRdata[^1] ^= 0x01;

        Assert.NotEqual(
            DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha256, dnsKeyRdata),
            DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha256, tamperedRdata)
        );
    }

    /// <summary>
    ///     This test independently recomputes RFC 4034 Appendix B.1's algorithm-1 special case
    ///     (the 4th/3rd-to-last octets of the modulus) itself, rather than re-deriving the same
    ///     answer <see cref="DnssecKeyTag" /> already computes, so a shared off-by-one bug
    ///     between the two implementations would not go unnoticed.
    /// </summary>
    [Fact]
    public void RsaMd5KeyTagUsesTheAppendixB1SpecialCase()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaMd5,
            out IDnssecSigningAlgorithm rsaMd5
        );
        byte[] publicKey = rsaMd5.GenerateKeyPair(1024).PublicKeyWireFormat;
        byte[] dnsKeyRdata = BuildDnsKeyRdata(DnssecAlgorithmEnum.RsaMd5, publicKey);

        int exponentLength = publicKey[0] == 0 ? (publicKey[1] << 8) | publicKey[2] : publicKey[0];
        int modulusStart = (publicKey[0] == 0 ? 3 : 1) + exponentLength;
        byte[] modulus = publicKey[modulusStart..];
        ushort expected = (ushort)((modulus[^3] << 8) | modulus[^2]);

        Assert.Equal(expected, DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaMd5, dnsKeyRdata));
    }

    [Theory]
    [InlineData((byte)1, 20)] // SHA-1
    [InlineData((byte)2, 32)] // SHA-256
    [InlineData((byte)4, 48)] // SHA-384
    public void DsDigestLengthMatchesTheAlgorithm(byte digestType, int expectedLength)
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha256,
            out IDnssecSigningAlgorithm rsaSha256
        );
        byte[] dnsKeyRdata = BuildDnsKeyRdata(
            DnssecAlgorithmEnum.RsaSha256,
            rsaSha256.GenerateKeyPair(1024).PublicKeyWireFormat
        );
        DnsName owner = DnsName.Parse("child.example.com");

        byte[] digest = DsDigestCodec.Compute(digestType, owner, dnsKeyRdata);

        Assert.Equal(expectedLength, digest.Length);
    }

    [Fact]
    public void DsDigestTypeThreeGostThrowsNotSupported()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha256,
            out IDnssecSigningAlgorithm rsaSha256
        );
        byte[] dnsKeyRdata = BuildDnsKeyRdata(
            DnssecAlgorithmEnum.RsaSha256,
            rsaSha256.GenerateKeyPair(1024).PublicKeyWireFormat
        );
        DnsName owner = DnsName.Parse("child.example.com");

        Assert.Throws<NotSupportedException>(() => DsDigestCodec.Compute(3, owner, dnsKeyRdata));
    }

    [Fact]
    public void DsDigestChangesWithTheOwnerNameHoldingRdataConstant()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha256,
            out IDnssecSigningAlgorithm rsaSha256
        );
        byte[] dnsKeyRdata = BuildDnsKeyRdata(
            DnssecAlgorithmEnum.RsaSha256,
            rsaSha256.GenerateKeyPair(1024).PublicKeyWireFormat
        );

        byte[] digestOne = DsDigestCodec.Compute(
            2,
            DnsName.Parse("child.example.com"),
            dnsKeyRdata
        );
        byte[] digestTwo = DsDigestCodec.Compute(
            2,
            DnsName.Parse("other.example.com"),
            dnsKeyRdata
        );

        Assert.NotEqual(digestOne, digestTwo);
    }

    [Fact]
    public void DsDigestIsDeterministic()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha256,
            out IDnssecSigningAlgorithm rsaSha256
        );
        byte[] dnsKeyRdata = BuildDnsKeyRdata(
            DnssecAlgorithmEnum.RsaSha256,
            rsaSha256.GenerateKeyPair(1024).PublicKeyWireFormat
        );
        DnsName owner = DnsName.Parse("child.example.com");

        Assert.Equal(
            DsDigestCodec.Compute(2, owner, dnsKeyRdata),
            DsDigestCodec.Compute(2, owner, dnsKeyRdata)
        );
    }
}

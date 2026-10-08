using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests that <see cref="RrsetSignatureVerifier" /> still verifies an RRset signed
///     with RSASHA1, an algorithm this server no longer signs with
///     (<see cref="DnssecAlgorithmRegistry.IsSigningProhibited" />). Validation of zones signed
///     elsewhere must keep working. The test signs the RRset itself with the registry's algorithm.
/// </summary>
public class RrsetSignatureVerifierWeakAlgorithmTests
{
    [Fact]
    public void TryVerify_ForAnRrsetSignedWithRsaSha1_StillSucceeds()
    {
        DnsName apex = DnsName.Parse("example.com");
        DnsName owner = DnsName.Parse("www.example.com");
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha1,
            out IDnssecSigningAlgorithm algorithm
        );
        DnssecKeyPair pair = algorithm.GenerateKeyPair(2048);

        byte[] keyRdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(true, false, DnssecAlgorithmEnum.RsaSha1, pair.PublicKeyWireFormat)
        );
        DnsResourceRecord dnsKey = new(
            apex,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            3600,
            keyRdata
        );
        List<DnsResourceRecord> rrset =
        [
            new DnsResourceRecord(
                owner,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(System.Net.IPAddress.Parse("192.0.2.1"))
                )
            ),
        ];

        DateTimeOffset now = DateTimeOffset.UtcNow;
        RrSigRecordData unsigned = new(
            DnsResourceRecordTypeEnum.A,
            DnssecAlgorithmEnum.RsaSha1,
            (byte)owner.Labels.Count,
            300,
            (uint)now.AddDays(1).ToUnixTimeSeconds(),
            (uint)now.AddDays(-1).ToUnixTimeSeconds(),
            DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha1, keyRdata),
            apex,
            []
        );
        byte[] signature = algorithm.Sign(
            DnssecCanonicalization.BuildSigningInput(unsigned, owner, DnsClassEnum.Internet, rrset),
            pair.PrivateKeyPkcs8
        );
        DnsResourceRecord rrsig = new(
            owner,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    unsigned.TypeCovered,
                    unsigned.Algorithm,
                    unsigned.Labels,
                    unsigned.OriginalTimeToLiveSeconds,
                    unsigned.SignatureExpiration,
                    unsigned.SignatureInception,
                    unsigned.KeyTag,
                    unsigned.SignerName,
                    signature
                )
            )
        );

        Assert.True(RrsetSignatureVerifier.TryVerify(rrset, [rrsig], [dnsKey], now));
    }
}

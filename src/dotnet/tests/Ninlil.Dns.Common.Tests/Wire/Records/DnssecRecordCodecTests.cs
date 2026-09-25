using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire.Records;

/// <summary>
///     This class tests the DNSSEC record codecs (DS/CDS, DNSKEY/CDNSKEY, RRSIG, NSEC, NSEC3 and
///     NSEC3PARAM) by round-tripping records and checking specific wire bytes.
/// </summary>
public class DnssecRecordCodecTests
{
    [Fact]
    public void DsRoundTripsThroughItsOwnCodec()
    {
        DsRecordData ds = new(12345, DnssecAlgorithmEnum.EcdsaP256Sha256, 2, [1, 2, 3, 4, 5]);

        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.DS, ds);
        DsRecordData decoded = (DsRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DS, wire);

        Assert.Equal(ds, decoded);
    }

    [Fact]
    public void CdsSharesDsExactWireShapeAsDistinctRecordType()
    {
        DsRecordData ds = new(12345, DnssecAlgorithmEnum.EcdsaP256Sha256, 2, [1, 2, 3, 4, 5]);

        byte[] dsWire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.DS, ds);
        byte[] cdsWire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Cds, ds);

        Assert.Equal(dsWire, cdsWire);

        DnsRecordDataCodecRegistry.TryGetCodec(
            DnsResourceRecordTypeEnum.DS,
            out IDnsRecordDataCodec dsCodec
        );
        DnsRecordDataCodecRegistry.TryGetCodec(
            DnsResourceRecordTypeEnum.Cds,
            out IDnsRecordDataCodec cdsCodec
        );

        Assert.NotEqual(dsCodec.RecordType, cdsCodec.RecordType);
        Assert.Equal(dsCodec.DataType, cdsCodec.DataType);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void DnsKeyRoundTripsWithFlagsPreserved(bool isZoneKey, bool isSecureEntryPoint)
    {
        DnsKeyRecordData key = new(
            isZoneKey,
            isSecureEntryPoint,
            DnssecAlgorithmEnum.Ed25519,
            [.. Enumerable.Range(0, 32).Select(i => (byte)i)]
        );

        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.DnsKey, key);

        Assert.Equal(3, wire[2]); // Protocol is always 3, per RFC 4034 §2.1.2.

        DnsKeyRecordData decoded = (DnsKeyRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, wire);

        Assert.Equal(key, decoded);
    }

    [Fact]
    public void CdnsKeySharesDnsKeyExactWireShape()
    {
        DnsKeyRecordData key = new(true, true, DnssecAlgorithmEnum.RsaSha256, [0xAA, 0xBB]);

        byte[] dnsKeyWire = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            key
        );
        byte[] cdnsKeyWire = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.CdnsKey,
            key
        );

        Assert.Equal(dnsKeyWire, cdnsKeyWire);
    }

    [Fact]
    public void RrSigRoundTripsAndSignerNameIsUncompressed()
    {
        RrSigRecordData rrsig = new(
            DnsResourceRecordTypeEnum.A,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            2,
            3600,
            1893456000,
            1861920000,
            54321,
            DnsName.Parse("example.com"),
            [.. Enumerable.Range(0, 64).Select(i => (byte)(i * 3))]
        );

        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.RrSig, rrsig);
        RrSigRecordData decoded = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, wire);

        Assert.Equal(rrsig, decoded);

        // Byte 18 onward is the Signer's Name, which RFC 4034 §3.1.7 forbids compressing, so
        // the top two bits of its first byte must not be set.
        Assert.True((wire[18] & 0xC0) == 0 || wire[18] == 0);
    }

    [Fact]
    public void NsecRoundTripsAcrossTwoBitmapWindows()
    {
        DnsName nextName = DnsName.Parse("next.example.com");
        List<DnsResourceRecordTypeEnum> types =
        [
            DnsResourceRecordTypeEnum.A,
            DnsResourceRecordTypeEnum.NS,
            DnsResourceRecordTypeEnum.RrSig,
            DnsResourceRecordTypeEnum.Nsec,
            DnsResourceRecordTypeEnum.Caa, // 257, which forces a second bitmap window.
        ];

        NsecRecordData nsec = new(nextName, types);
        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Nsec, nsec);
        NsecRecordData decoded = (NsecRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Nsec, wire);

        Assert.Equal(nextName, decoded.NextDomainName);
        Assert.Equal(types.OrderBy(t => (int)t), decoded.Types.OrderBy(t => (int)t));
    }

    [Fact]
    public void NsecBitmapMatchesHandVerifiedBytesForASimpleTypeSet()
    {
        // {A=1, NS=2}: window 0, bitmap length 1, byte 0x60
        // (bit index 1 -> 0x80>>1=0x40, bit index 2 -> 0x80>>2=0x20, combined 0x60).
        NsecRecordData nsec = new(
            DnsName.Root,
            [DnsResourceRecordTypeEnum.A, DnsResourceRecordTypeEnum.NS]
        );
        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Nsec, nsec);

        // Skip the Next Domain Name (root = 1 byte, the trailing zero label).
        byte[] bitmap = wire[1..];

        Assert.Equal(new byte[] { 0x00, 0x01, 0x60 }, bitmap);
    }

    [Fact]
    public void Nsec3RoundTripsWithOptOutSaltAndTypes()
    {
        Nsec3RecordData nsec3 = new(
            1,
            true,
            10,
            [0xDE, 0xAD, 0xBE, 0xEF],
            [.. Enumerable.Range(0, 20).Select(i => (byte)i)],
            [DnsResourceRecordTypeEnum.A, DnsResourceRecordTypeEnum.RrSig]
        );

        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Nsec3, nsec3);

        Assert.Equal(0x01, wire[1] & 0x01); // Opt-Out is the least significant Flags bit.

        Nsec3RecordData decoded = (Nsec3RecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Nsec3, wire);

        Assert.Equal(nsec3, decoded);
    }

    [Fact]
    public void Nsec3WithNoSaltNoOptOutAndNoTypesStillRoundTrips()
    {
        Nsec3RecordData nsec3 = new(1, false, 0, [], [1, 2, 3], []);

        byte[] wire = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Nsec3, nsec3);
        Nsec3RecordData decoded = (Nsec3RecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Nsec3, wire);

        Assert.Equal(nsec3, decoded);
    }

    [Fact]
    public void Nsec3ParamRoundTrips()
    {
        Nsec3ParamRecordData param = new(1, 15, [0x01, 0x02, 0x03]);

        byte[] wire = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.Nsec3Param,
            param
        );

        Assert.Equal(5 + 3, wire.Length);

        Nsec3ParamRecordData decoded = (Nsec3ParamRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Nsec3Param, wire);

        Assert.Equal(param, decoded);
    }
}

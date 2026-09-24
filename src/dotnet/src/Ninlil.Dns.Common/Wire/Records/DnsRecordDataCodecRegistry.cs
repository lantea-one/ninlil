using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class is the single place that knows which <see cref="IDnsRecordDataCodec" /> handles
///     which <see cref="DnsResourceRecordTypeEnum" />. A record type with no codec registered here
///     is simply carried as opaque bytes by <see cref="Wire.DnsResourceRecord" />, exactly as RFC
///     3597 requires for a type an implementation does not specifically understand -- which is
///     also how a genuinely unknown, future record type behaves without needing any code change
///     at all.
///     <para>
///         This registry is deliberately a static lookup table rather than something registered
///         through dependency injection: which codec handles which record type is fixed at
///         compile time, the same way <see cref="Ninlil.Common.Serializer.JsonSerializer.Converters" />
///         is a fixed, static list rather than a per-application configuration choice.
///     </para>
/// </summary>
public static class DnsRecordDataCodecRegistry
{
    /// <summary>
    ///     This property contains every registered codec, keyed by the record type it handles.
    /// </summary>
    private static readonly Dictionary<DnsResourceRecordTypeEnum, IDnsRecordDataCodec> Codecs =
        new()
        {
            [DnsResourceRecordTypeEnum.A] = new ARecordDataCodec(),
            [DnsResourceRecordTypeEnum.Aaaa] = new AaaaRecordDataCodec(),
            [DnsResourceRecordTypeEnum.NS] = new NsRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Cname] = new CnameRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Soa] = new SoaRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Ptr] = new PtrRecordDataCodec(),
            [DnsResourceRecordTypeEnum.MX] = new MxRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Txt] = new TxtRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Srv] = new SrvRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Caa] = new CaaRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Dname] = new DnameRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Svcb] = new SvcbRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Https] = new HttpsRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Naptr] = new NaptrRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Loc] = new LocRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Uri] = new UriRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Hinfo] = new HinfoRecordDataCodec(),
            [DnsResourceRecordTypeEnum.RP] = new RpRecordDataCodec(),
            [DnsResourceRecordTypeEnum.KX] = new KxRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Afsdb] = new AfsdbRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Apl] = new AplRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Eui48] = new Eui48RecordDataCodec(),
            [DnsResourceRecordTypeEnum.Eui64] = new Eui64RecordDataCodec(),
            [DnsResourceRecordTypeEnum.SshFp] = new SshfpRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Tlsa] = new TlsaRecordDataCodec(),
            [DnsResourceRecordTypeEnum.SmimeA] = new SmimeaRecordDataCodec(),
            [DnsResourceRecordTypeEnum.OpenPgpKey] = new OpenPgpKeyRecordDataCodec(),
            [DnsResourceRecordTypeEnum.IpSecKey] = new IpSecKeyRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Cert] = new CertRecordDataCodec(),
            [DnsResourceRecordTypeEnum.DS] = new DsRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Cds] = new CdsRecordDataCodec(),
            [DnsResourceRecordTypeEnum.DnsKey] = new DnsKeyRecordDataCodec(),
            [DnsResourceRecordTypeEnum.CdnsKey] = new CdnsKeyRecordDataCodec(),
            [DnsResourceRecordTypeEnum.RrSig] = new RrSigRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Nsec] = new NsecRecordDataCodec(),
            [DnsResourceRecordTypeEnum.Nsec3] = new Nsec3RecordDataCodec(),
            [DnsResourceRecordTypeEnum.Nsec3Param] = new Nsec3ParamRecordDataCodec(),
        };

    /// <summary>
    ///     This method looks up the codec registered for a record type.
    /// </summary>
    /// <param name="recordType">The record type to look up a codec for.</param>
    /// <param name="codec">The registered codec, if one exists.</param>
    /// <returns>A <see langword="bool" /> indicating whether a codec was found.</returns>
    public static bool TryGetCodec(
        DnsResourceRecordTypeEnum recordType,
        out IDnsRecordDataCodec codec
    ) => Codecs.TryGetValue(recordType, out codec);

    /// <summary>
    ///     This method decodes a record's RDATA into its strongly-typed model, if a codec is
    ///     registered for its type.
    /// </summary>
    /// <param name="recordType">The RDATA's record type.</param>
    /// <param name="rdata">The RDATA to decode.</param>
    /// <returns>
    ///     The decoded, strongly-typed record data, or <see langword="null" /> if no codec is
    ///     registered for <paramref name="recordType" />.
    /// </returns>
    public static IDnsRecordData Decode(DnsResourceRecordTypeEnum recordType, byte[] rdata) =>
        TryGetCodec(recordType, out IDnsRecordDataCodec codec) ? codec.Read(rdata) : null;

    /// <summary>
    ///     This method encodes a strongly-typed record data model into RDATA bytes.
    /// </summary>
    /// <param name="recordType">
    ///     The record type <paramref name="data" /> belongs to. This is not inferred from
    ///     <paramref name="data" />'s runtime type -- <see cref="ServiceBindingRecordData" />, for
    ///     example, is shared by two different record types -- so the caller, which already knows
    ///     which type it is encoding, states it explicitly.
    /// </param>
    /// <param name="data">The strongly-typed record data to encode.</param>
    /// <returns>The encoded RDATA.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no codec is registered for <paramref name="recordType" />.
    /// </exception>
    public static byte[] Encode(DnsResourceRecordTypeEnum recordType, IDnsRecordData data) =>
        TryGetCodec(recordType, out IDnsRecordDataCodec codec)
            ? codec.Write(data)
            : throw new InvalidOperationException(
                $"No codec is registered for record type '{recordType}'."
            );
}

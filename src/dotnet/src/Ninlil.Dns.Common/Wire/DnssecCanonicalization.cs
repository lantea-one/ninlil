using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class implements the canonical form and ordering rules of RFC 4034 §6: the one byte
///     representation every DNSSEC signer and validator must agree on. A signature over a
///     different but valid serialization of the same records (other compression, mixed case, a
///     different record order) would not verify. The authoritative signer uses it to compute
///     <see cref="Records.RrSigRecordData" /> signatures and the validating resolver uses it to
///     check them. It does not depend on the signing algorithm, so it lives in
///     <c>Ninlil.Dns.Common</c>.
/// </summary>
public static class DnssecCanonicalization
{
    /// <summary>
    ///     This property contains the record types whose RDATA holds a domain name that RFC 4034 §6.2
    ///     requires to be lower-cased in canonical form. It is the RFC's list limited to the types
    ///     Ninlil implements, as updated by RFC 6840 §5.1: the RRSIG's Signer's Name is lower-cased,
    ///     but the NSEC Next Domain Name is not, so <c>NSEC</c> is deliberately absent; obsolete types on the list (<c>MD</c>, <c>MF</c>, <c>MB</c>, <c>MG</c>,
    ///     <c>MR</c>, <c>MINFO</c>, <c>RT</c>, <c>PX</c>, <c>NXT</c>, <c>A6</c>) are not in
    ///     <see cref="DnsResourceRecordTypeEnum" />. <see cref="DnsResourceRecordTypeEnum.Hinfo" /> is on
    ///     the RFC's list but holds no name, so canonicalizing it changes nothing.
    /// </summary>
    private static readonly HashSet<DnsResourceRecordTypeEnum> TypesWithEmbeddedNames =
    [
        DnsResourceRecordTypeEnum.NS,
        DnsResourceRecordTypeEnum.Cname,
        DnsResourceRecordTypeEnum.Soa,
        DnsResourceRecordTypeEnum.Ptr,
        DnsResourceRecordTypeEnum.Hinfo,
        DnsResourceRecordTypeEnum.MX,
        DnsResourceRecordTypeEnum.RP,
        DnsResourceRecordTypeEnum.Afsdb,
        DnsResourceRecordTypeEnum.Naptr,
        DnsResourceRecordTypeEnum.KX,
        DnsResourceRecordTypeEnum.Srv,
        DnsResourceRecordTypeEnum.Dname,
        DnsResourceRecordTypeEnum.RrSig,
    ];

    /// <summary>
    ///     This method compares two names in the canonical order of RFC 4034 §6.1. Labels are compared
    ///     from the rightmost (most significant) one, each as a lower-cased, left-justified unsigned
    ///     octet string in which a shorter label that is a prefix of a longer one sorts first. NSEC
    ///     chains use this order to decide which name is "next".
    /// </summary>
    /// <param name="left">The first name to compare.</param>
    /// <param name="right">The second name to compare.</param>
    /// <returns>
    ///     A negative value if <paramref name="left" /> sorts first, zero if the names are
    ///     canonically identical, or a positive value if <paramref name="right" /> sorts first.
    /// </returns>
    public static int CompareCanonicalOrder(DnsName left, DnsName right)
    {
        // Lower-case both names (case never affects the order) and reverse their labels. RFC
        // 4034 §6.1 compares the rightmost label first ("com", then "example", then "www"),
        // so reversing turns that into a left-to-right walk.
        List<byte[]> leftLabels = [.. left.ToCanonicalLower().Labels.Reverse()];
        List<byte[]> rightLabels = [.. right.ToCanonicalLower().Labels.Reverse()];

        int commonCount = Math.Min(leftLabels.Count, rightLabels.Count);

        // The first label that differs decides the comparison.
        for (int i = 0; i < commonCount; i++)
        {
            int comparison = CompareOctetString(leftLabels[i], rightLabels[i]);

            if (comparison != 0)
                return comparison;
        }

        // All shared labels matched, so the name with fewer labels sorts first, the same rule
        // CompareOctetString applies within a label.
        return leftLabels.Count - rightLabels.Count;
    }

    /// <summary>
    ///     This method orders an RRset canonically (RFC 4034 §6.3): by each record's canonical RDATA
    ///     (see <see cref="ToCanonicalRdata" />) as a left-justified unsigned octet sequence, in which
    ///     a shorter RDATA that is a prefix of a longer one sorts first.
    /// </summary>
    /// <param name="rrset">
    ///     The records to order. They are expected to share one owner name, class and type, as an
    ///     RRset does; this method does not check that.
    /// </param>
    /// <returns>The same records, in canonical order.</returns>
    public static List<DnsResourceRecord> CanonicalOrder(IEnumerable<DnsResourceRecord> rrset) =>
        [
            .. rrset
                .Select(record =>
                    (record, canonicalRdata: ToCanonicalRdata(record.Type, record.Data))
                )
                .OrderBy(pair => pair.canonicalRdata, OctetStringComparer.Instance)
                .Select(pair => pair.record),
        ];

    /// <summary>
    ///     This method puts one record's RDATA in canonical form (RFC 4034 §6.2): it decodes it,
    ///     lower-cases the embedded names for types in <see cref="TypesWithEmbeddedNames" />, and
    ///     re-encodes it. RDATA of a type with no codec or no embedded name is returned as a copy.
    /// </summary>
    /// <param name="type">The record's type.</param>
    /// <param name="rdata">The record's RDATA, as it appears on the wire.</param>
    /// <returns>The RDATA in canonical form.</returns>
    public static byte[] ToCanonicalRdata(DnsResourceRecordTypeEnum type, byte[] rdata)
    {
        // A type with no embedded name or no codec is already canonical, so skip the round
        // trip.
        if (
            !TypesWithEmbeddedNames.Contains(type)
            || !DnsRecordDataCodecRegistry.TryGetCodec(type, out IDnsRecordDataCodec codec)
        )
            return [.. rdata];

        // Decoding is the only way to reach a name in the middle of the RDATA.
        IDnsRecordData canonicalized = LowercaseEmbeddedNames(type, codec.Read(rdata));

        return codec.Write(canonicalized);
    }

    /// <summary>
    ///     This method builds the signature input of RFC 4034 §3.1.8.1: the RRSIG RDATA without the
    ///     Signature field, followed by every record of the RRset in canonical form (§6.2) and
    ///     canonical order (§6.3).
    /// </summary>
    /// <param name="rrsigMetadata">
    ///     The signature metadata: every <see cref="RrSigRecordData" /> field except
    ///     <see cref="RrSigRecordData.Signature" />, which is ignored.
    /// </param>
    /// <param name="ownerName">
    ///     The signed RRset's owner name as it appears in the message. When the RRSIG's Labels field
    ///     is smaller than the name's label count the RRset was synthesized from a wildcard, and the
    ///     signed owner is rebuilt with <see cref="ReconstructSignedOwner" /> (RFC 4035 §5.3.2).
    /// </param>
    /// <param name="recordClass">The signed RRset's class.</param>
    /// <param name="rrset">The records of the signed RRset.</param>
    /// <returns>The bytes to pass to the signing algorithm.</returns>
    /// <exception cref="ArgumentException">
    ///     The RRSIG's Labels field is larger than the label count of <paramref name="ownerName" />,
    ///     so no signed owner exists (RFC 4035 §5.3.1).
    /// </exception>
    public static byte[] BuildSigningInput(
        RrSigRecordData rrsigMetadata,
        DnsName ownerName,
        DnsClassEnum recordClass,
        IReadOnlyList<DnsResourceRecord> rrset
    )
    {
        ArrayBufferWriter<byte> buffer = new();

        // The RRSIG RDATA comes first, rebuilt with an empty Signature field, since a signature
        // cannot cover its own value.
        RrSigRecordData rrsigWithoutSignature = new(
            rrsigMetadata.TypeCovered,
            rrsigMetadata.Algorithm,
            rrsigMetadata.Labels,
            rrsigMetadata.OriginalTimeToLiveSeconds,
            rrsigMetadata.SignatureExpiration,
            rrsigMetadata.SignatureInception,
            rrsigMetadata.KeyTag,
            rrsigMetadata.SignerName.ToCanonicalLower(),
            []
        );
        WriteBytes(
            buffer,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                rrsigWithoutSignature
            )
        );

        // RFC 4034 §3.1.8.1: the signer's name above is in canonical (lower-case) form. The owner
        // is the name the signer signed, which for a wildcard expansion is the wildcard itself.
        DnsName signedOwnerName =
            ReconstructSignedOwner(ownerName, rrsigMetadata.Labels)
            ?? throw new ArgumentException(
                $"The RRSIG Labels value {rrsigMetadata.Labels} exceeds the label count of '{ownerName}'.",
                nameof(rrsigMetadata)
            );
        DnsName canonicalOwnerName = signedOwnerName.ToCanonicalLower();

        // Each record, in canonical order, adds its owner name, header and RDATA in wire form,
        // with every field canonical: lower-cased owner name, the RRSIG's original TTL instead
        // of the record's current TTL, and canonical RDATA. That makes the input independent
        // of compression or a cache's counted-down TTL.
        foreach (DnsResourceRecord record in CanonicalOrder(rrset))
        {
            byte[] canonicalRdata = ToCanonicalRdata(record.Type, record.Data);

            UncompressedDnsNameCodec.Write(buffer, canonicalOwnerName);

            Span<byte> header = buffer.GetSpan(10);
            BinaryPrimitives.WriteUInt16BigEndian(header[..2], (ushort)record.Type);
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), (ushort)recordClass);
            BinaryPrimitives.WriteUInt32BigEndian(
                header.Slice(4, 4),
                rrsigMetadata.OriginalTimeToLiveSeconds
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                header.Slice(8, 2),
                (ushort)canonicalRdata.Length
            );
            buffer.Advance(10);

            WriteBytes(buffer, canonicalRdata);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method rebuilds the owner name that an RRSIG was computed over (RFC 4035 §5.3.2). The
    ///     Labels field counts the owner's labels without a leading <c>*</c>. If it equals that
    ///     count, the owner is the name itself. If it is smaller, the RRset was synthesized from a
    ///     wildcard, and the signed owner is <c>*</c> followed by the rightmost
    ///     <paramref name="rrsigLabels" /> labels.
    /// </summary>
    /// <param name="ownerName">The RRset's owner name as it appears in the message.</param>
    /// <param name="rrsigLabels">The RRSIG's Labels field.</param>
    /// <returns>
    ///     The signed owner name, or <see langword="null" /> if <paramref name="rrsigLabels" /> is
    ///     larger than the label count, which makes the RRSIG invalid.
    /// </returns>
    public static DnsName ReconstructSignedOwner(DnsName ownerName, byte rrsigLabels)
    {
        int count = ownerName.Labels.Count;

        if (count > 0 && ownerName.Labels[0] is [(byte)'*'])
            count--;

        if (rrsigLabels == count)
            return ownerName;

        if (rrsigLabels > count)
            return null;

        // Build the name from labels, not text: a text join would mishandle the root and escapes.
        DnsName closestEncloser = new([
            .. ownerName.Labels.Skip(ownerName.Labels.Count - rrsigLabels),
        ]);

        return closestEncloser.PrependLabels("*");
    }

    /// <summary>
    ///     This method applies <see cref="DnsName.ToCanonicalLower" /> to every domain name in a
    ///     decoded record's RDATA, for the types that have one.
    /// </summary>
    private static IDnsRecordData LowercaseEmbeddedNames(
        DnsResourceRecordTypeEnum type,
        IDnsRecordData data
    ) =>
        type switch
        {
            DnsResourceRecordTypeEnum.NS => ((NsRecordData)data) with
            {
                NameServer = ((NsRecordData)data).NameServer.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Cname => ((CnameRecordData)data) with
            {
                Target = ((CnameRecordData)data).Target.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Soa => ((SoaRecordData)data) with
            {
                PrimaryNameServer = ((SoaRecordData)data).PrimaryNameServer.ToCanonicalLower(),
                ResponsiblePersonMailbox = (
                    (SoaRecordData)data
                ).ResponsiblePersonMailbox.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Ptr => ((PtrRecordData)data) with
            {
                Target = ((PtrRecordData)data).Target.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.MX => ((MxRecordData)data) with
            {
                Exchange = ((MxRecordData)data).Exchange.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.RP => ((RpRecordData)data) with
            {
                Mailbox = ((RpRecordData)data).Mailbox.ToCanonicalLower(),
                TextReference = ((RpRecordData)data).TextReference.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Afsdb => ((AfsdbRecordData)data) with
            {
                Hostname = ((AfsdbRecordData)data).Hostname.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Naptr => ((NaptrRecordData)data) with
            {
                Replacement = ((NaptrRecordData)data).Replacement.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.KX => ((KxRecordData)data) with
            {
                Exchanger = ((KxRecordData)data).Exchanger.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Srv => ((SrvRecordData)data) with
            {
                Target = ((SrvRecordData)data).Target.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.Dname => ((DnameRecordData)data) with
            {
                Target = ((DnameRecordData)data).Target.ToCanonicalLower(),
            },
            DnsResourceRecordTypeEnum.RrSig => LowercaseRrSigSignerName((RrSigRecordData)data),
            _ => data,
        };

    /// <summary>
    ///     This method lower-cases the Signer's Name of an <see cref="RrSigRecordData" />. It is
    ///     separate from the switch above because <see cref="RrSigRecordData" /> is a plain class and
    ///     has no <c>with</c> expression.
    /// </summary>
    private static RrSigRecordData LowercaseRrSigSignerName(RrSigRecordData data) =>
        new(
            data.TypeCovered,
            data.Algorithm,
            data.Labels,
            data.OriginalTimeToLiveSeconds,
            data.SignatureExpiration,
            data.SignatureInception,
            data.KeyTag,
            data.SignerName.ToCanonicalLower(),
            data.Signature
        );

    /// <summary>
    ///     This method compares two byte sequences as RFC 4034 §6.1 and §6.3 require: a
    ///     left-justified unsigned octet comparison in which a missing octet (a shorter sequence that
    ///     is a prefix of a longer one) sorts before a zero octet.
    /// </summary>
    private static int CompareOctetString(byte[] left, byte[] right)
    {
        int commonLength = Math.Min(left.Length, right.Length);

        for (int i = 0; i < commonLength; i++)
            if (left[i] != right[i])
                return left[i] - right[i];

        return left.Length - right.Length;
    }

    /// <summary>
    ///     This method appends a byte sequence to a buffer.
    /// </summary>
    private static void WriteBytes(IBufferWriter<byte> buffer, byte[] bytes)
    {
        bytes.CopyTo(buffer.GetSpan(bytes.Length));
        buffer.Advance(bytes.Length);
    }

    /// <summary>
    ///     This class adapts <see cref="CompareOctetString" /> to <see cref="IComparer{T}" /> for
    ///     use with <see cref="Enumerable.OrderBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey},IComparer{TKey})" />.
    /// </summary>
    private sealed class OctetStringComparer : IComparer<byte[]>
    {
        /// <summary>
        ///     This field contains the shared instance of this stateless comparer.
        /// </summary>
        public static readonly OctetStringComparer Instance = new();

        /// <inheritdoc />
        public int Compare(byte[] x, byte[] y) => CompareOctetString(x, y);
    }
}

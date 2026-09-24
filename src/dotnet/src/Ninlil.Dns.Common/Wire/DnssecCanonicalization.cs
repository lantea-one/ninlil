using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class implements RFC 4034 §6's canonical form and ordering rules -- the exact,
///     unambiguous byte representation every DNSSEC signer and validator must agree on, since a
///     signature computed over one valid-but-different serialization of the same records (a
///     different name compression choice, mixed letter case, or a different record order) would
///     simply fail to verify. This is shared groundwork the authoritative signing pipeline needs
///     for computing <see cref="Records.RrSigRecordData" /> signatures, and the validating
///     resolver needs for the opposite direction; it has no dependency on which specific signing
///     algorithm is used, so it belongs here in <c>Ninlil.Dns.Common</c> rather than in an
///     algorithm-specific project.
/// </summary>
public static class DnssecCanonicalization
{
    /// <summary>
    ///     This property contains every record type whose RDATA embeds a domain name that RFC
    ///     4034 §6.2 requires to be lower-cased as part of that record's canonical form --
    ///     transcribed directly from the RFC's own list, restricted to the types this project
    ///     actually implements (the RFC's list also names several 1980s-era types, such as
    ///     <c>MD</c>/<c>MF</c>/<c>MB</c>/<c>MG</c>/<c>MR</c>/<c>MINFO</c>/<c>RT</c>/<c>PX</c>/
    ///     <c>NXT</c>/<c>A6</c>, that <see cref="DnsResourceRecordTypeEnum" /> deliberately leaves
    ///     out as long-obsolete). <see cref="DnsResourceRecordTypeEnum.Hinfo" /> is included for
    ///     completeness with the RFC's own list even though its RDATA has no domain name to
    ///     lower-case at all -- canonicalizing it is simply a no-op.
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
        DnsResourceRecordTypeEnum.Nsec,
    ];

    /// <summary>
    ///     This method compares two names in RFC 4034 §6.1's canonical DNS name order: labels
    ///     compared most-significant (rightmost) first, each label treated as a lower-cased,
    ///     left-justified unsigned octet string in which a shorter label that is a prefix of a
    ///     longer one sorts first -- the same rule <see cref="Nsec3RecordData" />/
    ///     <see cref="NsecRecordData" /> chains rely on to know which name is "next".
    /// </summary>
    /// <param name="left">The first name to compare.</param>
    /// <param name="right">The second name to compare.</param>
    /// <returns>
    ///     A negative value if <paramref name="left" /> sorts first, zero if the names are
    ///     canonically identical, or a positive value if <paramref name="right" /> sorts first.
    /// </returns>
    public static int CompareCanonicalOrder(DnsName left, DnsName right)
    {
        // What: lower-case both names first (case never affects canonical order) and reverse
        // each name's own label list. Why/how: RFC 4034 §6.1 compares most-significant label
        // first -- that's the *rightmost* label as written ("com" before "example" before "www"
        // in "www.example.com") -- so reversing turns that into an ordinary left-to-right walk.
        List<byte[]> leftLabels = [.. left.ToCanonicalLower().Labels.Reverse()];
        List<byte[]> rightLabels = [.. right.ToCanonicalLower().Labels.Reverse()];

        int commonCount = Math.Min(leftLabels.Count, rightLabels.Count);

        // Compare label-by-label from the most significant end; the first label that differs
        // decides the whole comparison, exactly like an ordinary lexicographic string compare.
        for (int i = 0; i < commonCount; i++)
        {
            int comparison = CompareOctetString(leftLabels[i], rightLabels[i]);

            if (comparison != 0)
                return comparison;
        }

        // Every shared label matched, so whichever name has fewer labels is the other's prefix
        // and sorts first -- the same "shorter-is-a-prefix sorts first" rule CompareOctetString
        // itself applies to the bytes within a single label.
        return leftLabels.Count - rightLabels.Count;
    }

    /// <summary>
    ///     This method reorders an RRset into RFC 4034 §6.3's canonical order: by the RDATA
    ///     portion of each record's own canonical form (see <see cref="ToCanonicalRdata" />),
    ///     treated as a left-justified unsigned octet sequence in which a shorter RDATA that is a
    ///     prefix of a longer one sorts first.
    /// </summary>
    /// <param name="rrset">
    ///     The records to order -- expected to already share one owner name, class, and type, the
    ///     way every real RRset does, though this method does not itself enforce that.
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
    ///     This method canonicalizes a single record's RDATA per RFC 4034 §6.2: decoding it,
    ///     lower-casing every embedded domain name <see cref="TypesWithEmbeddedNames" /> lists for
    ///     this record's type, and re-encoding it. A record whose type carries no codec, or
    ///     carries no embedded name, is returned unchanged (as a defensive copy).
    /// </summary>
    /// <param name="type">The record's type.</param>
    /// <param name="rdata">The record's RDATA, exactly as it appears on the wire.</param>
    /// <returns>The RDATA in canonical form.</returns>
    public static byte[] ToCanonicalRdata(DnsResourceRecordTypeEnum type, byte[] rdata)
    {
        // A type with no embedded name (or no registered codec at all) has nothing this method
        // could change, so its RDATA is already in canonical form as-is -- skip the
        // decode/re-encode round trip entirely rather than doing wasted work for the common case.
        if (
            !TypesWithEmbeddedNames.Contains(type)
            || !DnsRecordDataCodecRegistry.TryGetCodec(type, out IDnsRecordDataCodec codec)
        )
            return [.. rdata];

        // Decode the RDATA into its structured form, lower-case whichever name field(s) it
        // carries, then re-encode -- the only way to reach and rewrite a name embedded partway
        // through an otherwise-opaque RDATA blob.
        IDnsRecordData canonicalized = LowercaseEmbeddedNames(type, codec.Read(rdata));

        return codec.Write(canonicalized);
    }

    /// <summary>
    ///     This method builds the exact byte sequence RFC 4034 §3.1.8.1 defines as a signature's
    ///     input: the covering <see cref="RrSigRecordData" />'s own RDATA (every field except the
    ///     Signature field itself, which obviously cannot be part of its own input), followed by
    ///     every record in the signed RRset, each in the canonical form §6.2 defines and ordered
    ///     per §6.3.
    /// </summary>
    /// <param name="rrsigMetadata">
    ///     The signature's own metadata -- every <see cref="RrSigRecordData" /> field except
    ///     <see cref="RrSigRecordData.Signature" />, whose value here is ignored.
    /// </param>
    /// <param name="ownerName">The signed RRset's owner name.</param>
    /// <param name="recordClass">The signed RRset's class.</param>
    /// <param name="rrset">The signed RRset's own records.</param>
    /// <returns>The bytes to pass to the signing algorithm.</returns>
    public static byte[] BuildSigningInput(
        RrSigRecordData rrsigMetadata,
        DnsName ownerName,
        DnsClassEnum recordClass,
        IReadOnlyList<DnsResourceRecord> rrset
    )
    {
        ArrayBufferWriter<byte> buffer = new();

        // The signature's own metadata comes first in the signing input, with the Signature
        // field itself zeroed out -- a signature cannot cover its own not-yet-computed value, so
        // this rebuilds the RRSIG's RDATA with an empty signature before encoding it.
        RrSigRecordData rrsigWithoutSignature = new(
            rrsigMetadata.TypeCovered,
            rrsigMetadata.Algorithm,
            rrsigMetadata.Labels,
            rrsigMetadata.OriginalTimeToLiveSeconds,
            rrsigMetadata.SignatureExpiration,
            rrsigMetadata.SignatureInception,
            rrsigMetadata.KeyTag,
            rrsigMetadata.SignerName,
            []
        );
        WriteBytes(
            buffer,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                rrsigWithoutSignature
            )
        );

        DnsName canonicalOwnerName = ownerName.ToCanonicalLower();

        // Every record in the RRset, visited in canonical order, contributes its own
        // owner-name-plus-resource-record-header-plus-RDATA slice to the signing input -- the
        // same wire shape a real resource record has, but with every field forced to its
        // canonical form (lower-cased owner name, this record's own original TTL rather than
        // whatever TTL it happens to carry, canonical RDATA) so the signature is reproducible
        // regardless of transient wire-encoding choices like compression or a stale cached TTL.
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
    ///     This method applies <see cref="DnsName.ToCanonicalLower" /> to every domain name
    ///     embedded in a decoded record's RDATA, for the specific record types that have one.
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
            DnsResourceRecordTypeEnum.Nsec => ((NsecRecordData)data) with
            {
                NextDomainName = ((NsecRecordData)data).NextDomainName.ToCanonicalLower(),
            },
            _ => data,
        };

    /// <summary>
    ///     This method lower-cases an <see cref="RrSigRecordData" />'s Signer's Name -- pulled
    ///     out of the switch above since <see cref="RrSigRecordData" /> is a plain class, not a
    ///     record, so it has no compiler-generated <c>with</c> expression to lean on.
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
    ///     This method compares two byte sequences as RFC 4034 §6.1/§6.3 require: a left-justified
    ///     unsigned octet comparison in which the absence of an octet (a shorter sequence that is
    ///     a prefix of a longer one) sorts before an explicit zero octet.
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
    ///     This method appends a raw byte sequence to a buffer.
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
        ///     This field contains the single shared instance of this stateless comparer.
        /// </summary>
        public static readonly OctetStringComparer Instance = new();

        /// <inheritdoc />
        public int Compare(byte[] x, byte[] y) => CompareOctetString(x, y);
    }
}

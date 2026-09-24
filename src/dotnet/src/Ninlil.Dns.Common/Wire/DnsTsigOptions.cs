using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents the TSIG extension to a DNS message, defined by RFC 8945: a
///     transaction-level authentication tag, computed with a shared secret, proving the message
///     was neither forged nor altered in transit. Like <see cref="DnsEdnsOptions" />, this
///     information does not travel as a header field; it rides inside a pseudo-record of type
///     TSIG, which must be the last record in the message's Additional section. A
///     <see cref="DnsMessage" /> with no TSIG record present has no <see cref="DnsTsigOptions" />
///     at all (its <see cref="DnsMessage.Tsig" /> property is <see langword="null" />).
///     <para>
///         This is a plain class rather than a record, since <see cref="Mac" /> and
///         <see cref="OtherData" /> are <see langword="byte[]" /> and a compiler-generated
///         equality would compare them by reference rather than by content.
///     </para>
/// </summary>
/// <param name="keyName">
///     The name identifying which shared secret was used, matched against a pre-shared key an
///     operator configured -- not necessarily a real, resolvable domain name.
/// </param>
/// <param name="algorithmName">
///     The MAC algorithm used, as a domain name per RFC 8945's convention -- Ninlil only
///     ever produces and accepts <see cref="HmacSha256AlgorithmName" />.
/// </param>
/// <param name="timeSigned">
///     When the MAC was generated, as seconds since the Unix epoch. Only the low 48 bits are
///     significant on the wire.
/// </param>
/// <param name="fudge">How many seconds of clock skew between signer and verifier are tolerated.</param>
/// <param name="mac">The message authentication code itself.</param>
/// <param name="originalId">
///     The original message ID, copied here so a resolver that rewrote the header's ID (an
///     intermediate forwarder, for example) does not invalidate the signature.
/// </param>
/// <param name="error">
///     A TSIG-specific outcome, per RFC 8945 §5.3 -- <see cref="DnsResponseCodeEnum.NoError" />
///     on a successfully verified request, or one of
///     <see cref="DnsResponseCodeEnum.BadVersionOrBadSignature" /> (BADSIG, reusing the same
///     numeric value RFC 6891 assigns to EDNS's BADVERS), <see cref="DnsResponseCodeEnum.BadKey" />,
///     or <see cref="DnsResponseCodeEnum.BadTime" /> when a server rejects a signed request and
///     reports why.
/// </param>
/// <param name="otherData">
///     Additional data relevant to <see cref="Error" /> -- populated only when
///     <see cref="Error" /> is <see cref="DnsResponseCodeEnum.BadTime" />, with the responder's
///     own current time, so the requestor can resynchronize its clock.
/// </param>
public class DnsTsigOptions(
    DnsName keyName,
    DnsName algorithmName,
    ulong timeSigned,
    ushort fudge,
    byte[] mac,
    ushort originalId,
    DnsResponseCodeEnum error,
    byte[] otherData
) : IEquatable<DnsTsigOptions>
{
    /// <summary>
    ///     This property contains the well-known algorithm name for HMAC-SHA256, the only MAC
    ///     algorithm Ninlil implements -- a deliberate choice to support HMAC-SHA256 only
    ///     rather than also accepting the legacy HMAC-SHA1/MD5 algorithms.
    /// </summary>
    public static DnsName HmacSha256AlgorithmName { get; } = DnsName.Parse("hmac-sha256");

    /// <summary>
    ///     This property contains the name identifying which shared secret was used.
    /// </summary>
    public DnsName KeyName { get; } = keyName;

    /// <summary>
    ///     This property contains the MAC algorithm used.
    /// </summary>
    public DnsName AlgorithmName { get; } = algorithmName;

    /// <summary>
    ///     This property contains when the MAC was generated, as seconds since the Unix epoch.
    /// </summary>
    public ulong TimeSigned { get; } = timeSigned;

    /// <summary>
    ///     This property contains how many seconds of clock skew are tolerated.
    /// </summary>
    public ushort Fudge { get; } = fudge;

    /// <summary>
    ///     This property contains the message authentication code itself.
    /// </summary>
    public byte[] Mac { get; } = mac;

    /// <summary>
    ///     This property contains the original message ID.
    /// </summary>
    public ushort OriginalId { get; } = originalId;

    /// <summary>
    ///     This property contains this TSIG record's own outcome.
    /// </summary>
    public DnsResponseCodeEnum Error { get; } = error;

    /// <summary>
    ///     This property contains additional data relevant to <see cref="Error" />.
    /// </summary>
    public byte[] OtherData { get; } = otherData;

    /// <inheritdoc />
    public bool Equals(DnsTsigOptions other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (
                KeyName == other.KeyName
                && AlgorithmName == other.AlgorithmName
                && TimeSigned == other.TimeSigned
                && Fudge == other.Fudge
                && Mac.AsSpan().SequenceEqual(other.Mac)
                && OriginalId == other.OriginalId
                && Error == other.Error
                && OtherData.AsSpan().SequenceEqual(other.OtherData)
            )
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DnsTsigOptions);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(KeyName);
        hash.Add(AlgorithmName);
        hash.Add(TimeSigned);
        hash.Add(Fudge);

        foreach (byte b in Mac)
            hash.Add(b);

        hash.Add(OriginalId);
        hash.Add(Error);

        foreach (byte b in OtherData)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

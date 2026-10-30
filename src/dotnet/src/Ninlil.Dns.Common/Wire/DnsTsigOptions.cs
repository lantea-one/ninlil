using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents the TSIG extension of a DNS message (RFC 8945): an authentication
///     code, computed with a shared secret, that proves the message was not forged or altered.
///     Like <see cref="DnsEdnsOptions" />, it is not a header field but a TSIG pseudo-record, which
///     must be the last record in the Additional section. A <see cref="DnsMessage" /> without one
///     has a <see langword="null" /> <see cref="DnsMessage.Tsig" />. <see cref="TsigCodec" />
///     builds the bytes the MAC covers.
///     <para>
///         This is a plain class rather than a record because <see cref="Mac" /> and
///         <see cref="OtherData" /> are <see langword="byte[]" />, and compiler-generated equality
///         would compare them by reference instead of by content.
///     </para>
/// </summary>
/// <param name="keyName">
///     The name of the shared secret that was used, matched against a key an operator
///     configured. It need not be a resolvable domain name.
/// </param>
/// <param name="algorithmName">
///     The MAC algorithm, written as a domain name as RFC 8945 does. Ninlil produces and accepts
///     the HMAC-SHA1, -SHA256, -SHA384 and -SHA512 names declared on this class.
/// </param>
/// <param name="timeSigned">
///     When the MAC was generated, as seconds since the Unix epoch. Only the low 48 bits are
///     significant on the wire.
/// </param>
/// <param name="fudge">How many seconds of clock skew between signer and verifier are tolerated.</param>
/// <param name="mac">The message authentication code.</param>
/// <param name="originalId">
///     The message ID at signing time. RFC 8945 carries it so a verifier can restore it if a
///     forwarder rewrote the header ID; the MAC is computed over the message with this ID in the
///     header (see <see cref="TsigCodec.ExtractSignedMessageBytes" />).
/// </param>
/// <param name="error">
///     The TSIG outcome (the RCODEs of RFC 8945 §3): <see cref="DnsResponseCodeEnum.NoError" />
///     for a verified request, or <see cref="DnsResponseCodeEnum.BadVersionOrBadSignature" /> (BADSIG, which shares
///     its value with the EDNS BADVERS code), <see cref="DnsResponseCodeEnum.BadKey" />,
///     <see cref="DnsResponseCodeEnum.BadTruncation" /> or <see cref="DnsResponseCodeEnum.BadTime" />
///     when a server rejects a signed request.
/// </param>
/// <param name="otherData">
///     Extra data for <see cref="Error" />. It is only filled when <see cref="Error" /> is
///     <see cref="DnsResponseCodeEnum.BadTime" />, with the responder's current time, so the
///     requester can correct its clock.
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
    ///     This property contains the algorithm name for HMAC-SHA1, which RFC 8945 §6 lists as
    ///     mandatory to implement.
    /// </summary>
    public static DnsName HmacSha1AlgorithmName { get; } = DnsName.Parse("hmac-sha1");

    /// <summary>
    ///     This property contains the algorithm name for HMAC-SHA256, the default for a new key.
    /// </summary>
    public static DnsName HmacSha256AlgorithmName { get; } = DnsName.Parse("hmac-sha256");

    /// <summary>
    ///     This property contains the algorithm name for HMAC-SHA384.
    /// </summary>
    public static DnsName HmacSha384AlgorithmName { get; } = DnsName.Parse("hmac-sha384");

    /// <summary>
    ///     This property contains the algorithm name for HMAC-SHA512.
    /// </summary>
    public static DnsName HmacSha512AlgorithmName { get; } = DnsName.Parse("hmac-sha512");

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
    ///     This property contains the message authentication code.
    /// </summary>
    public byte[] Mac { get; } = mac;

    /// <summary>
    ///     This property contains the original message ID.
    /// </summary>
    public ushort OriginalId { get; } = originalId;

    /// <summary>
    ///     This property contains this TSIG record's outcome.
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

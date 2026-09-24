using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This type carries the outcome of attempting to read a PROXY protocol header (v1 or v2)
///     off the start of a connection, via <see cref="ProxyProtocolReader.ReadAsync" />.
///     <para>
///         Three distinct outcomes exist, not two: a header can be genuinely absent or malformed
///         (<see cref="IsValid" /> is <see langword="false" />, and per the PROXY protocol
///         specification's own guidance -- "any sequence which does not exactly match the protocol
///         must be discarded and cause the receiver to abort the connection" -- the caller must
///         close the connection rather than fall back to the real TCP peer address); a header can
///         be valid but deliberately carry no usable client address (a v2 LOCAL command, used for
///         a proxy's own health checks, or a v1 <c>UNKNOWN</c> family) in which case
///         <see cref="ClaimedSourceAddress" /> is <see langword="null" /> and the caller should
///         fall back to the connection's own real peer address without treating this as an error;
///         or a header can be valid and carry a genuine claimed client address.
///     </para>
/// </summary>
public readonly struct ProxyProtocolParseResult
{
    /// <summary>
    ///     This property contains a shared instance representing a missing or malformed header --
    ///     the connection must be aborted rather than answered.
    /// </summary>
    public static ProxyProtocolParseResult Failed { get; } =
        new(isValid: false, claimedSourceAddress: null);

    /// <summary>
    ///     This property contains a shared instance representing a structurally valid header that
    ///     deliberately carries no usable client address (a health-check LOCAL connection, or a
    ///     v1 <c>UNKNOWN</c> family) -- the connection proceeds normally, using its own real peer
    ///     address.
    /// </summary>
    public static ProxyProtocolParseResult WithoutClaimedAddress { get; } =
        new(isValid: true, claimedSourceAddress: null);

    /// <summary>
    ///     This method builds a result representing a structurally valid header carrying a real
    ///     claimed client address.
    /// </summary>
    public static ProxyProtocolParseResult WithClaimedAddress(IPAddress address) =>
        new(isValid: true, claimedSourceAddress: address);

    private ProxyProtocolParseResult(bool isValid, IPAddress claimedSourceAddress)
    {
        IsValid = isValid;
        ClaimedSourceAddress = claimedSourceAddress;
    }

    /// <summary>
    ///     This property is <see langword="true" /> for a structurally valid header (whether or
    ///     not it carries a usable address), and <see langword="false" /> for a missing or
    ///     malformed one that requires the connection to be aborted.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    ///     This property contains the client address the header claims on the connection's own
    ///     behalf, or <see langword="null" /> when the header is valid but carries none (see this
    ///     type's own doc comment) -- meaningless when <see cref="IsValid" /> is
    ///     <see langword="false" />.
    /// </summary>
    public IPAddress ClaimedSourceAddress { get; }
}

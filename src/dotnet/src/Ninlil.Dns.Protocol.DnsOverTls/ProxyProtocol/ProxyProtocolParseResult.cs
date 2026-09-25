using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This struct carries the outcome of reading a PROXY protocol header (v1 or v2) off the start
///     of a connection with <see cref="ProxyProtocolReader.ReadAsync" />. There are three outcomes:
///     <list type="bullet">
///         <item>
///             The header is missing or malformed (<see cref="IsValid" /> is
///             <see langword="false" />). The PROXY protocol specification says "any sequence which
///             does not exactly match the protocol must be discarded and cause the receiver to
///             abort the connection", so the caller closes the connection rather than fall back to
///             the TCP peer address.
///         </item>
///         <item>
///             The header is valid but carries no client address (a v2 LOCAL command, used for a
///             proxy's health checks, a v1 <c>UNKNOWN</c> line, or a v2 family other than TCP over
///             IPv4 or IPv6). <see cref="ClaimedSourceAddress" /> is <see langword="null" /> and
///             the caller uses the connection's peer address; this is not an error.
///         </item>
///         <item>The header is valid and carries a claimed client address.</item>
///     </list>
/// </summary>
public readonly struct ProxyProtocolParseResult
{
    /// <summary>
    ///     This property contains the result for a missing or malformed header; the connection must
    ///     be aborted rather than answered.
    /// </summary>
    public static ProxyProtocolParseResult Failed { get; } =
        new(isValid: false, claimedSourceAddress: null);

    /// <summary>
    ///     This property contains the result for a valid header that carries no usable client
    ///     address; the connection proceeds with its peer address.
    /// </summary>
    public static ProxyProtocolParseResult WithoutClaimedAddress { get; } =
        new(isValid: true, claimedSourceAddress: null);

    /// <summary>
    ///     This method builds the result for a valid header carrying a claimed client address.
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
    ///     This property contains the client address the header claims, or <see langword="null" />
    ///     when the header is valid but carries none. It has no meaning when <see cref="IsValid" />
    ///     is <see langword="false" />.
    /// </summary>
    public IPAddress ClaimedSourceAddress { get; }
}

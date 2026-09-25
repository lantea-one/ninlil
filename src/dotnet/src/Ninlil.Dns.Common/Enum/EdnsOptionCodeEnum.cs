using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the EDNS(0) option codes assigned by IANA in the "DNS EDNS0 Option
///     Codes" registry. Options travel in the RDATA of the OPT pseudo-record (RFC 6891) and attach
///     metadata to a query or response without changing the core message format. Only the options
///     Ninlil recognizes are listed. Any other code is still read and carried as raw bytes, because
///     RFC 6891 requires an unknown option to be ignored rather than treated as an error.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<EdnsOptionCodeEnum>>]
public enum EdnsOptionCodeEnum : ushort
{
    /// <summary>
    ///     A long-lived query, defined by the expired LLQ draft (later RFC 8764).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "LLQ")]
    LongLivedQuery = 1,

    /// <summary>
    ///     An update lease, defined by the (expired) UL draft.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "UL")]
    UpdateLease = 2,

    /// <summary>
    ///     A name server identifier, defined by RFC 5001. Lets a resolver ask a server to identify
    ///     itself, which helps debug an anycast deployment where many servers share one address.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NSID")]
    NameServerIdentifier = 3,

    /// <summary>
    ///     DNSSEC algorithm understood, defined by RFC 6975.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DAU")]
    DnssecAlgorithmUnderstood = 5,

    /// <summary>
    ///     DS hash algorithm understood, defined by RFC 6975.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DHU")]
    DsHashUnderstood = 6,

    /// <summary>
    ///     NSEC3 hash algorithm understood, defined by RFC 6975.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "N3U")]
    Nsec3HashUnderstood = 7,

    /// <summary>
    ///     The client's subnet, defined by RFC 7871. Lets a recursive resolver tell an authoritative
    ///     server roughly where the client is, so the answer (often a CDN's nearest edge) can suit
    ///     that location.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "edns-client-subnet")]
    ClientSubnet = 8,

    /// <summary>
    ///     A zone's expiry, defined by RFC 7314.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "EDNS EXPIRE")]
    Expire = 9,

    /// <summary>
    ///     A DNS Cookie, defined by RFC 7873. A client and server exchange cookie values that make
    ///     off-path spoofing much harder without the cost of full cryptographic authentication.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "COOKIE")]
    Cookie = 10,

    /// <summary>
    ///     A request to keep a TCP, TLS, or QUIC connection open, defined by RFC 7828.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "edns-tcp-keepalive")]
    TcpKeepalive = 11,

    /// <summary>
    ///     Padding, defined by RFC 7830. Pads an encrypted query or response (DoT/DoH/DoQ) to a
    ///     block size, so an observer cannot guess the query from the packet length.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Padding")]
    Padding = 12,

    /// <summary>
    ///     A DNSSEC chain query, defined by RFC 7901.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CHAIN")]
    Chain = 13,

    /// <summary>
    ///     A DNSSEC key tag, defined by RFC 8145.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "edns-key-tag")]
    KeyTag = 14,

    /// <summary>
    ///     An extended DNS error, defined by RFC 8914. Lets a server say in machine-readable form
    ///     why a query failed (for example "blocked" or "DNSSEC signature expired"), which the RCODE
    ///     alone cannot express.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Extended DNS Error")]
    ExtendedDnsError = 15,
}

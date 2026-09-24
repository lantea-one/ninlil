using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the EDNS(0) option codes assigned by IANA in the "DNS EDNS0 Option
///     Codes" registry. These travel inside the RDATA of the OPT pseudo-record defined by RFC
///     6891, each carrying a small piece of side-channel metadata a resolver or server wants to
///     attach to a query or response without changing the core DNS message format itself. Only
///     the options Ninlil actually recognizes are listed here; an option code that isn't
///     (there are dozens, most vendor-specific or experimental) is still read and carried along
///     as raw bytes rather than rejected, since RFC 6891 requires an unrecognized option to be
///     ignored, not treated as an error.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<EdnsOptionCodeEnum>>]
public enum EdnsOptionCodeEnum : ushort
{
    /// <summary>
    ///     A long-lived query, defined by the (expired) LLQ draft. Kept for interoperability with
    ///     the small number of clients that still send it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "LLQ")]
    LongLivedQuery = 1,

    /// <summary>
    ///     An update lease, defined by the (expired) UL draft.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "UL")]
    UpdateLease = 2,

    /// <summary>
    ///     A name server identifier, defined by RFC 5001. Lets a resolver ask a server to
    ///     identify itself, useful for debugging an anycast deployment where many physical
    ///     servers answer under one address.
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
    ///     The client's subnet, defined by RFC 7871. Lets a recursive resolver tell an
    ///     authoritative server roughly where the original client is, so the server can return an
    ///     answer (commonly a CDN's closest edge address) tailored to that location.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "edns-client-subnet")]
    ClientSubnet = 8,

    /// <summary>
    ///     A zone's expiry, defined by RFC 7314.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "EDNS EXPIRE")]
    Expire = 9,

    /// <summary>
    ///     A DNS Cookie, defined by RFC 7873. A lightweight, pre-shared value exchanged between a
    ///     client and server that makes off-path response spoofing significantly harder without
    ///     the overhead of a full cryptographic exchange.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "COOKIE")]
    Cookie = 10,

    /// <summary>
    ///     A request to keep a TCP, TLS, or QUIC connection open, defined by RFC 7828.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "edns-tcp-keepalive")]
    TcpKeepalive = 11,

    /// <summary>
    ///     Padding, defined by RFC 7830. Pads an encrypted query or response (DoT/DoH/DoQ) out to
    ///     a fixed size, so an observer watching only the encrypted packet lengths cannot guess
    ///     which query was asked from its size alone.
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
    ///     An extended DNS error, defined by RFC 8914. Lets a server explain, in machine-readable
    ///     form, more specifically *why* a query failed (for example, "blocked by policy" versus
    ///     "DNSSEC signature expired") than the base RCODE alone can express.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Extended DNS Error")]
    ExtendedDnsError = 15,
}

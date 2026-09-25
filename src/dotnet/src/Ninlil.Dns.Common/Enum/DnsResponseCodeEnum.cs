using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the DNS RCODE values assigned by IANA: the number a server puts in a
///     response header to say what happened. RFC 1035 gave the field four bits. EDNS(0) (RFC 6891)
///     widened it to twelve bits, so finer-grained failures (mostly TSIG and DNS Cookies) can be
///     reported without confusing software that only knows the first sixteen values.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsResponseCodeEnum>>]
public enum DnsResponseCodeEnum : ushort
{
    /// <summary>
    ///     The query succeeded, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NOERROR")]
    NoError = 0,

    /// <summary>
    ///     The name server could not interpret the query, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "FORMERR")]
    FormatError = 1,

    /// <summary>
    ///     The name server encountered an internal problem while answering, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SERVFAIL")]
    ServerFailure = 2,

    /// <summary>
    ///     The queried name does not exist, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NXDOMAIN")]
    NxDomain = 3,

    /// <summary>
    ///     The name server does not support the requested kind of query, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NOTIMP")]
    NotImplemented = 4,

    /// <summary>
    ///     The name server refused to answer for policy reasons, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "REFUSED")]
    Refused = 5,

    /// <summary>
    ///     A dynamic update's prerequisite claimed a name should not exist, but it does, defined
    ///     by RFC 2136.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "YXDOMAIN")]
    YxDomain = 6,

    /// <summary>
    ///     A dynamic update's prerequisite claimed a record set should not exist, but it does,
    ///     defined by RFC 2136.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "YXRRSET")]
    YxRrSet = 7,

    /// <summary>
    ///     A dynamic update's prerequisite claimed a record set should exist, but it does not,
    ///     defined by RFC 2136.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NXRRSET")]
    NxRrSet = 8,

    /// <summary>
    ///     The server is not authoritative for the zone named in a dynamic update or is not
    ///     authorized to answer a TSIG-signed query, defined by RFC 2136 and RFC 8945.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NOTAUTH")]
    NotAuthoritative = 9,

    /// <summary>
    ///     A name used in a dynamic update was outside the zone specified in the update,
    ///     defined by RFC 2136.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NOTZONE")]
    NotZone = 10,

    /// <summary>
    ///     The server does not support this DSO type, defined by RFC 8490.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DSOTYPENI")]
    DsoTypeNotImplemented = 11,

    /// <summary>
    ///     IANA gives code 16 two names for two contexts. As an EDNS(0) extended RCODE (RFC 6891) it
    ///     means the requester's EDNS version is not supported ("BADVERS"). In the error field of a
    ///     TSIG record (RFC 8945) it means the signature failed verification ("BADSIG"). One member
    ///     covers both, since two members with the same value could not be told apart; the context
    ///     (OPT pseudo-record or TSIG record) says which meaning applies.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADVERS")]
    BadVersionOrBadSignature = 16,

    /// <summary>
    ///     A TSIG key was not recognized, defined by RFC 8945.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADKEY")]
    BadKey = 17,

    /// <summary>
    ///     A signature's timestamp was outside its acceptable window, defined by RFC 8945.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADTIME")]
    BadTime = 18,

    /// <summary>
    ///     A TKEY mode was not supported, defined by RFC 2930.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADMODE")]
    BadMode = 19,

    /// <summary>
    ///     A TKEY name was already in use, defined by RFC 2930.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADNAME")]
    BadName = 20,

    /// <summary>
    ///     An algorithm was not supported, defined by RFC 2930.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADALG")]
    BadAlgorithm = 21,

    /// <summary>
    ///     A TSIG or TKEY record was truncated, defined by RFC 8945.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADTRUNC")]
    BadTruncation = 22,

    /// <summary>
    ///     The DNS Cookie in the request was missing or did not match, defined by RFC 7873.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BADCOOKIE")]
    BadCookie = 23,
}

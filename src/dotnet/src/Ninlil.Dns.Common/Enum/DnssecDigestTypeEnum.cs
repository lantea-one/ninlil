using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the digest algorithms IANA has assigned for use inside a
///     <c>DS</c> record -- the hash a parent zone takes of a child zone's key, rather than storing
///     the key itself. Values are drawn from the "Delegation Signer (DS) Resource Record (RR) Type
///     Digest Algorithms" registry.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecDigestTypeEnum>>]
public enum DnssecDigestTypeEnum : byte
{
    /// <summary>
    ///     SHA-1, defined by RFC 3658. Weak by modern standards; kept for reading legacy DS
    ///     records but never produced for a newly-signed zone.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SHA-1")]
    Sha1 = 1,

    /// <summary>
    ///     SHA-256, defined by RFC 4509. Ninlil's recommended default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SHA-256")]
    Sha256 = 2,

    /// <summary>
    ///     GOST R 34.11-94, defined by RFC 5933.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "GOST-R-34.11-94")]
    GostR341194 = 3,

    /// <summary>
    ///     SHA-384, defined by RFC 6605.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SHA-384")]
    Sha384 = 4,
}

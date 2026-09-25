using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the digest algorithms IANA has assigned for <c>DS</c> records. A DS
///     record holds a hash of the child zone's key instead of the key itself. Values come from the
///     "Delegation Signer (DS) Resource Record (RR) Type Digest Algorithms" registry.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecDigestTypeEnum>>]
public enum DnssecDigestTypeEnum : byte
{
    /// <summary>
    ///     SHA-1, defined by RFC 3658. Weak by modern standards, and kept so older DS records can be
    ///     read.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SHA-1")]
    Sha1 = 1,

    /// <summary>
    ///     SHA-256, defined by RFC 4509. The recommended choice.
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

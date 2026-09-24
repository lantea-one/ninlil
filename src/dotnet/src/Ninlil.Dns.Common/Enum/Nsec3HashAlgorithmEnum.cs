using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the hash algorithms IANA has assigned for use by
///     <c>NSEC3</c>/<c>NSEC3PARAM</c> records, defined by RFC 5155. There is, as of this writing,
///     only one: SHA-1 is used here purely as an obfuscation step to prevent zone-walking, not as
///     a cryptographic signature, so it remains fit for purpose even though it is unsuitable for
///     DNSSEC signing itself.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<Nsec3HashAlgorithmEnum>>]
public enum Nsec3HashAlgorithmEnum : byte
{
    /// <summary>
    ///     SHA-1, defined by RFC 5155.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SHA-1")]
    Sha1 = 1,
}

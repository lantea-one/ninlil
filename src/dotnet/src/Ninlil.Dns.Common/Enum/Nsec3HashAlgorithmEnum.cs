using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the hash algorithms IANA has assigned for <c>NSEC3</c> and
///     <c>NSEC3PARAM</c> records (RFC 5155). SHA-1 is the only one. NSEC3 uses it to obscure owner
///     names against zone walking, not as a signature, so it stays fit for this purpose even though
///     it is unsuitable for DNSSEC signing.
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

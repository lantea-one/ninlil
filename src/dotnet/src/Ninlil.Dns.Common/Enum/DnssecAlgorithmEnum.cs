using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the DNSSEC signing algorithms assigned by IANA in the "Domain Name
///     System Security (DNSSEC) Algorithm Numbers" registry. RFC 9904 §3 sorts them into algorithms
///     an implementation <b>must</b> support, ones it <b>should</b>, and broken ones that
///     <b>must not</b> be used for signing. It marks 5 and 7 NOT RECOMMENDED for signing, and
///     this server refuses them as its own rule. The broken ones are listed so older signed zones can
///     still be parsed, reported on and validated. This server never signs with algorithms 1, 3,
///     5, 6, 7 and 12. Which of the others may be used for a new key is set by
///     <c>DnssecAlgorithmPolicyOptions.AllowedAlgorithms</c>.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecAlgorithmEnum>>]
public enum DnssecAlgorithmEnum : byte
{
    /// <summary>
    ///     RSA/MD5, defined by RFC 3110. Broken; RFC 9904 forbids using it to sign or validate.
    ///     Listed so older signed zones still parse.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RSAMD5")]
    RsaMd5 = 1,

    /// <summary>
    ///     DSA/SHA1, defined by RFC 3755. Deprecated; RFC 9904 forbids signing new zones with it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DSA")]
    Dsa = 3,

    /// <summary>
    ///     RSA/SHA-1, defined by RFC 3110. RFC 9904 permits validating existing signatures but
    ///     marks it NOT RECOMMENDED for signing new zones. This server does not sign with it as its
    ///     own rule.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RSASHA1")]
    RsaSha1 = 5,

    /// <summary>
    ///     DSA/SHA1 using NSEC3-aware key identifiers, defined by RFC 5155. Carries the same
    ///     RFC 9904 deprecation as <see cref="Dsa" />.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DSA-NSEC3-SHA1")]
    DsaNsec3Sha1 = 6,

    /// <summary>
    ///     RSA/SHA-1 using NSEC3-aware key identifiers, defined by RFC 5155. Carries the same
    ///     RFC 9904 §3 guidance as <see cref="RsaSha1" />, and this server does not sign with it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RSASHA1-NSEC3-SHA1")]
    RsaSha1Nsec3Sha1 = 7,

    /// <summary>
    ///     RSA/SHA-256, defined by RFC 5702. RFC 9904 §3 marks it MUST for signing.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RSASHA256")]
    RsaSha256 = 8,

    /// <summary>
    ///     RSA/SHA-512, defined by RFC 5702.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RSASHA512")]
    RsaSha512 = 10,

    /// <summary>
    ///     GOST R 34.10-2001, defined by RFC 5933. Regionally required in some jurisdictions.
    ///     RFC 9904 §3 marks it MUST NOT for signing and MAY for validation; this server can
    ///     neither sign nor validate with it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ECC-GOST")]
    EccGost = 12,

    /// <summary>
    ///     ECDSA using the P-256 curve and SHA-256, defined by RFC 6605. RFC 9904 §3 marks it MUST
    ///     for signing, and it is the recommended choice for new zones.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ECDSAP256SHA256")]
    EcdsaP256Sha256 = 13,

    /// <summary>
    ///     ECDSA using the P-384 curve and SHA-384, defined by RFC 6605.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ECDSAP384SHA384")]
    EcdsaP384Sha384 = 14,

    /// <summary>
    ///     Ed25519, defined by RFC 8080. Fast to compute and produces very small signatures.
    ///     RFC 9904 §3 marks it RECOMMENDED for signing.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ED25519")]
    Ed25519 = 15,

    /// <summary>
    ///     Ed448, defined by RFC 8080.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ED448")]
    Ed448 = 16,
}

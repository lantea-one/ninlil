using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists every DNS resource record TYPE Ninlil understands, with the values
///     IANA assigns in the "Resource Record (RR) TYPEs" registry. On the wire a type is this
///     two-byte number. Obsolete or experimental early types (such as <c>MD</c>, <c>MB</c> or
///     <c>NSAP</c>) are left out, since a modern server has no reason to originate them.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsResourceRecordTypeEnum>>]
public enum DnsResourceRecordTypeEnum : ushort
{
    /// <summary>
    ///     A host address. This is the record that maps a name to an IPv4 address, defined by
    ///     RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "A")]
    A = 1,

    /// <summary>
    ///     An authoritative name server for a zone, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NS")]
    NS = 2,

    /// <summary>
    ///     The canonical name for an alias, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CNAME")]
    Cname = 5,

    /// <summary>
    ///     The start of a zone of authority, defined by RFC 1035. Every zone has exactly one of
    ///     these records at its apex, and it carries the serial number and timing parameters used
    ///     for zone transfers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SOA")]
    Soa = 6,

    /// <summary>
    ///     A domain name pointer, most commonly used for reverse DNS lookups, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "PTR")]
    Ptr = 12,

    /// <summary>
    ///     Host information, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HINFO")]
    Hinfo = 13,

    /// <summary>
    ///     Mail exchange, defined by RFC 1035. Points a domain at the server responsible for
    ///     accepting email on its behalf.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "MX")]
    MX = 15,

    /// <summary>
    ///     Free-form text, defined by RFC 1035. Widely repurposed today for machine-readable
    ///     verification tokens such as SPF and DKIM policies.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TXT")]
    Txt = 16,

    /// <summary>
    ///     Responsible person, defined by RFC 1183.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RP")]
    RP = 17,

    /// <summary>
    ///     AFS database location, defined by RFC 1183.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AFSDB")]
    Afsdb = 18,

    /// <summary>
    ///     A cryptographic signature, defined by RFC 2535 and re-used by SIG(0) transaction
    ///     signatures (RFC 2931).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SIG")]
    Sig = 24,

    /// <summary>
    ///     A public key, defined by RFC 2535.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "KEY")]
    Key = 25,

    /// <summary>
    ///     A host address, but for IPv6 instead of IPv4, defined by RFC 3596.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AAAA")]
    Aaaa = 28,

    /// <summary>
    ///     Location information (latitude, longitude, and altitude), defined by RFC 1876.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "LOC")]
    Loc = 29,

    /// <summary>
    ///     A service locator, defined by RFC 2782. Advertises the host and port responsible for a
    ///     named service, such as <c>_sip._tcp</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SRV")]
    Srv = 33,

    /// <summary>
    ///     A naming authority pointer, defined by RFC 3403. Used by protocols such as ENUM and
    ///     SIP to rewrite a domain name into a URI via regular expressions.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NAPTR")]
    Naptr = 35,

    /// <summary>
    ///     A key exchanger, defined by RFC 2230.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "KX")]
    KX = 36,

    /// <summary>
    ///     A certificate, defined by RFC 4398.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CERT")]
    Cert = 37,

    /// <summary>
    ///     A non-terminal DNS name redirection, defined by RFC 6672. Unlike <see cref="Cname" />,
    ///     it rewrites only the suffix of a query name, allowing an entire subtree to be aliased
    ///     at once.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DNAME")]
    Dname = 39,

    /// <summary>
    ///     The pseudo-record that carries EDNS(0) metadata (the requester's UDP payload size,
    ///     extended flags, and options such as Cookies), defined by RFC 6891. It never appears in zone
    ///     data, only in the additional section of a message.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "OPT")]
    Opt = 41,

    /// <summary>
    ///     An address prefix list, defined by RFC 3123.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "APL")]
    Apl = 42,

    /// <summary>
    ///     A delegation signer, defined by RFC 4034. This is the record a parent zone publishes to
    ///     vouch for a child zone's DNSSEC key, forming the chain of trust.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DS")]
    DS = 43,

    /// <summary>
    ///     An SSH public key fingerprint, defined by RFC 4255. Lets a client verify an SSH host
    ///     key out-of-band via DNSSEC instead of trusting it on first use.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SSHFP")]
    SshFp = 44,

    /// <summary>
    ///     An IPsec key, defined by RFC 4025.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "IPSECKEY")]
    IpSecKey = 45,

    /// <summary>
    ///     A DNSSEC signature, defined by RFC 4034. Every signed record set is accompanied by one
    ///     of these, carrying the cryptographic signature that a resolver validates.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RRSIG")]
    RrSig = 46,

    /// <summary>
    ///     The "next secure" record, defined by RFC 4034. Authenticates the non-existence of a
    ///     name by listing, in signed form, the next name that does exist in the zone.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NSEC")]
    Nsec = 47,

    /// <summary>
    ///     A DNSSEC public key, defined by RFC 4034.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DNSKEY")]
    DnsKey = 48,

    /// <summary>
    ///     A DHCP identifier, defined by RFC 4701. Lets a DHCP server publish the record that
    ///     "owns" a dynamically-updated name, so two clients can't fight over the same one.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DHCID")]
    Dhcid = 49,

    /// <summary>
    ///     A hashed "next secure" record, defined by RFC 5155. Does the same job as
    ///     <see cref="Nsec" />, but hashes the owner names first so that a resolver cannot walk the
    ///     zone by repeatedly asking for nonexistent names.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NSEC3")]
    Nsec3 = 50,

    /// <summary>
    ///     The parameters (hash algorithm, iterations, and salt) used to compute a zone's
    ///     <see cref="Nsec3" /> records, defined by RFC 5155.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NSEC3PARAM")]
    Nsec3Param = 51,

    /// <summary>
    ///     A TLSA certificate association, defined by RFC 6698 as part of DANE. Pins the
    ///     certificate a TLS client should expect for a service, anchored by DNSSEC instead of the
    ///     public certificate-authority system.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TLSA")]
    Tlsa = 52,

    /// <summary>
    ///     An S/MIME certificate association, defined by RFC 8162. The same idea as
    ///     <see cref="Tlsa" />, applied to an email address's S/MIME certificate instead of a TLS
    ///     service.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SMIMEA")]
    SmimeA = 53,

    /// <summary>
    ///     A host identity protocol record, defined by RFC 8005.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HIP")]
    Hip = 55,

    /// <summary>
    ///     A child copy of a <see cref="DS" /> record, defined by RFC 7344. Published by a child
    ///     zone so its parent can pick it up automatically instead of requiring a manual DS update.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CDS")]
    Cds = 59,

    /// <summary>
    ///     A child copy of a <see cref="DnsKey" /> record, defined by RFC 7344, used for the same
    ///     automated-DS-update workflow as <see cref="Cds" />.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CDNSKEY")]
    CdnsKey = 60,

    /// <summary>
    ///     An OpenPGP public key, defined by RFC 7929.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "OPENPGPKEY")]
    OpenPgpKey = 61,

    /// <summary>
    ///     A child-to-parent synchronization record, defined by RFC 7477. Tells the parent zone
    ///     which of the child's NS/A/AAAA records it is safe to copy up automatically.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CSYNC")]
    Csync = 62,

    /// <summary>
    ///     A zone message digest, defined by RFC 8976. Lets anyone with a copy of a zone verify that
    ///     it matches what the zone's owner published.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ZONEMD")]
    ZoneMd = 63,

    /// <summary>
    ///     Service binding, defined by RFC 9460. Advertises connection parameters, such as supported
    ///     protocol versions or an alternate port, for a service of any protocol.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SVCB")]
    Svcb = 64,

    /// <summary>
    ///     HTTPS service binding, defined by RFC 9460. The same mechanism as <see cref="Svcb" />,
    ///     specialized for HTTPS. It lets a browser learn that a domain supports HTTP/3 before it
    ///     opens a connection.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HTTPS")]
    Https = 65,

    /// <summary>
    ///     A 48-bit extended unique identifier, defined by RFC 7043.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "EUI48")]
    Eui48 = 108,

    /// <summary>
    ///     A 64-bit extended unique identifier, defined by RFC 7043.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "EUI64")]
    Eui64 = 109,

    /// <summary>
    ///     A transaction key, defined by RFC 2930. Used to establish the shared secret that a
    ///     subsequent <see cref="Tsig" /> exchange signs with.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TKEY")]
    TKey = 249,

    /// <summary>
    ///     A transaction signature, defined by RFC 8945. Authenticates a single message, most often a
    ///     zone transfer or dynamic update, with a shared secret instead of public-key signatures.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TSIG")]
    Tsig = 250,

    /// <summary>
    ///     Not a stored record, but the QTYPE a secondary server sends to request an
    ///     incremental zone transfer, defined by RFC 1995.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "IXFR")]
    Ixfr = 251,

    /// <summary>
    ///     Not a stored record, but the QTYPE a secondary server sends to request a full
    ///     zone transfer, defined by RFC 5936.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AXFR")]
    Axfr = 252,

    /// <summary>
    ///     Not a stored record, but the QTYPE a client sends to ask for every record set
    ///     that exists at a name, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ANY")]
    Any = 255,

    /// <summary>
    ///     A uniform resource identifier, defined by RFC 7553.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "URI")]
    Uri = 256,

    /// <summary>
    ///     A certification authority authorization, defined by RFC 8659. Lets a domain owner
    ///     declare which certificate authorities are allowed to issue certificates for it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CAA")]
    Caa = 257,

    /// <summary>
    ///     A DNSSEC trust authority record. Never standardized in an RFC; kept for legacy zones that
    ///     still carry one.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TA")]
    Ta = 32768,

    /// <summary>
    ///     A DNSSEC lookaside validation record, defined by the historic RFC 4431. Lookaside
    ///     validation is deprecated; the value is kept so legacy zone data round-trips instead of
    ///     being rejected.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DLV")]
    Dlv = 32769,
}

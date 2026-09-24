using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes the role a zone plays on this server. It is Ninlil's own
///     operational vocabulary rather than an IANA-assigned wire value -- the DNS protocol itself
///     has no "zone type" field -- but the concepts themselves (primary, secondary, stub, and
///     forwarding zones) are long-standing, cross-implementation DNS operations terminology, and
///     catalog zones follow the IETF's DNS Catalog Zones specification.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ZoneTypeEnum>>]
public enum ZoneTypeEnum
{
    /// <summary>
    ///     This server holds the master, editable copy of the zone.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Primary")]
    Primary = 0,

    /// <summary>
    ///     This server holds a read-only copy of the zone, kept in sync with a primary server via
    ///     zone transfer (RFC 5936 <c>AXFR</c> or RFC 1995 <c>IXFR</c>) and RFC 1996
    ///     <c>NOTIFY</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Secondary")]
    Secondary = 1,

    /// <summary>
    ///     Like <see cref="Secondary" />, but this server only transfers the zone's <c>NS</c>
    ///     records rather than the full zone, purely to learn who the authoritative name servers
    ///     are.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Stub")]
    Stub = 2,

    /// <summary>
    ///     This server holds no data for the zone at all; instead, it forwards every query for
    ///     names under it to another, explicitly configured set of name servers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Forwarder")]
    Forwarder = 3,

    /// <summary>
    ///     A <see cref="Secondary" /> zone whose data happens to be a forwarding configuration
    ///     transferred in from a primary, rather than authored locally.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SecondaryForwarder")]
    SecondaryForwarder = 4,

    /// <summary>
    ///     A zone whose records are not DNS answers at all, but a machine-readable list of other
    ///     zones this server should provision automatically, per the IETF DNS Catalog Zones
    ///     specification.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Catalog")]
    Catalog = 5,

    /// <summary>
    ///     A <see cref="Secondary" /> copy of a <see cref="Catalog" /> zone.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SecondaryCatalog")]
    SecondaryCatalog = 6,

    /// <summary>
    ///     This server holds the master, editable copy of the zone, and continuously pushes its
    ///     own changes out to a configured cloud DNS provider (Cloudflare, Route 53, DigitalOcean)
    ///     via that provider's own REST API -- the operator's own choice, made once at zone
    ///     creation time, of which side is authoritative. Unlike <see cref="Primary" />, this
    ///     zone's data flows outward to a provider rather than out to RFC 5936/1996 secondaries.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CloudPrimary")]
    CloudPrimary = 7,

    /// <summary>
    ///     This server holds a read-only copy of a zone whose master, editable copy lives at a
    ///     configured cloud DNS provider, kept in sync by periodically pulling that provider's own
    ///     current record set via its REST API -- the cloud-provider analogue of
    ///     <see cref="Secondary" />, but polling a REST API instead of receiving RFC 5936/1996
    ///     zone transfers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CloudSecondary")]
    CloudSecondary = 8,
}

using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes the role a zone plays on this server. The DNS protocol has no
///     "zone type" field, so this is Ninlil vocabulary, but primary, secondary, stub and
///     forwarder zones are common DNS terms, and catalog zones follow the DNS Catalog Zones
///     specification (RFC 9432).
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
    ///     Like <see cref="Secondary" />, but only the zone's <c>NS</c> records are transferred, to
    ///     learn which servers are authoritative.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Stub")]
    Stub = 2,

    /// <summary>
    ///     This server holds no data for the zone. It forwards every query for names under it to a
    ///     configured set of name servers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Forwarder")]
    Forwarder = 3,

    /// <summary>
    ///     A <see cref="Secondary" /> zone whose data is a forwarding configuration transferred from a
    ///     primary, rather than authored locally.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SecondaryForwarder")]
    SecondaryForwarder = 4,

    /// <summary>
    ///     A zone whose records are not DNS answers but a machine-readable list of other zones to
    ///     provision automatically, as defined by DNS Catalog Zones.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Catalog")]
    Catalog = 5,

    /// <summary>
    ///     A <see cref="Secondary" /> copy of a <see cref="Catalog" /> zone.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SecondaryCatalog")]
    SecondaryCatalog = 6,

    /// <summary>
    ///     This server holds the editable copy of the zone and pushes changes to a configured cloud
    ///     DNS provider (Cloudflare, Route 53, DigitalOcean) through its REST API. Which side is
    ///     authoritative is chosen when the zone is created. Unlike <see cref="Primary" />, the data
    ///     flows to a provider rather than to RFC 5936/1996 secondaries.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CloudPrimary")]
    CloudPrimary = 7,

    /// <summary>
    ///     This server holds a read-only copy of a zone whose editable copy lives at a cloud DNS
    ///     provider. It stays in sync by polling the provider's REST API: the cloud counterpart of
    ///     <see cref="Secondary" />, without RFC 5936/1996 zone transfers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CloudSecondary")]
    CloudSecondary = 8,
}

using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration identifies which of Ninlil's own automated record-management
///     features created a given <c>RecordEntity</c> -- Ninlil's own operational vocabulary,
///     not an IANA-assigned value. It exists so a feature that reconciles a zone's record set
///     against some external source of truth (CAA auto-management and PTR/reverse-zone sync are
///     the two such features today) can tell its own previously-created records apart from one an
///     operator added by hand, and therefore only ever add/update/remove records it created
///     itself -- the same provenance-marker reasoning behind <c>RecordEntity</c>'s own
///     pre-existing <c>ExcludeFromSync</c> field, but for "who owns this record's lifecycle"
///     rather than "should this record ever leave/enter this server." Deliberately shared and
///     extensible: a future automated feature adds its own new value here rather than a
///     second, parallel marker field.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<RecordManagedByFeatureEnum>>]
public enum RecordManagedByFeatureEnum
{
    /// <summary>
    ///     This record is a CAA record created and kept in sync by the CAA
    ///     auto-management feature, reconciling a zone's CAA record set against its own
    ///     configured <c>CaaIssuerDomain</c> whenever the zone's ACME enrollment or CA selection
    ///     changes.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Caa")]
    Caa = 0,

    /// <summary>
    ///     This record is a PTR record created and kept in sync by the PTR/reverse-zone
    ///     sync feature, mirroring a <c>Primary</c>/<c>CloudPrimary</c> forward zone's own A/AAAA
    ///     record set into the enclosing <c>in-addr.arpa</c>/<c>ip6.arpa</c> reverse zone
    ///     whenever that forward record set changes.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Ptr")]
    Ptr = 1,

    /// <summary>
    ///     This record is a well-known IETF Catalog Zones (RFC 9432) record -- the schema
    ///     "version" TXT record at a catalog zone's own apex, or one member's own
    ///     <c>&lt;unique-N&gt;.zones.&lt;catalog&gt;</c> PTR record -- created and kept in sync by
    ///     <c>CatalogZoneMembershipService</c> whenever a zone's own <c>MemberOfCatalogZoneId</c>
    ///     is set or cleared.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Catalog")]
    Catalog = 2,

    /// <summary>
    ///     This record is an A/AAAA record created and kept in sync by the DHCP-&gt;DNS
    ///     integration feature, registering a DHCP client's own presented host name against the
    ///     address it currently leases -- created on lease grant, updated in place if the
    ///     client's address or host name changes on a later renewal, and removed on release or
    ///     expiry. See <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Dhcp")]
    Dhcp = 3,

    /// <summary>
    ///     This record is a TXT record created and kept in sync by the forward-zone content
    ///     sync feature, encoding one <c>ZoneTypeEnum.Forwarder</c> zone's own configured
    ///     <c>ZoneEntity.ForwarderAddresses</c> entry at that zone's apex, so a
    ///     <c>ZoneTypeEnum.SecondaryForwarder</c> zone has real, versioned, AXFR/IXFR-transferable
    ///     wire content to pull. See
    ///     <c>Ninlil.Dns.Server.Authoritative.Forwarding.ForwarderZoneContentSyncService</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ForwarderAddress")]
    ForwarderAddress = 4,
}

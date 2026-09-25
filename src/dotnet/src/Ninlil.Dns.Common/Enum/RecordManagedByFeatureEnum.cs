using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration identifies which automated record-management feature created a
///     <c>RecordEntity</c>. It is Ninlil vocabulary, not an IANA-assigned value. A feature that
///     reconciles a zone against another source of truth uses it to tell its records apart from
///     records an operator added by hand, so it only adds, updates or removes records it created.
///     (<c>RecordEntity.ExcludeFromSync</c> answers a different question: whether a record stays
///     local during zone transfer and cloud sync.) A new automated feature adds a value here rather
///     than a new marker field.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<RecordManagedByFeatureEnum>>]
public enum RecordManagedByFeatureEnum
{
    /// <summary>
    ///     A CAA record kept in sync by CAA auto-management, which reconciles the zone's CAA records
    ///     with its <c>CaaIssuerDomain</c> when the zone's ACME enrollment or CA selection changes.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Caa")]
    Caa = 0,

    /// <summary>
    ///     A PTR record kept in sync by reverse-zone sync, which mirrors the A/AAAA records of a
    ///     <c>Primary</c> or <c>CloudPrimary</c> forward zone into the enclosing <c>in-addr.arpa</c>
    ///     or <c>ip6.arpa</c> zone whenever they change.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Ptr")]
    Ptr = 1,

    /// <summary>
    ///     A Catalog Zones (RFC 9432) record: the schema "version" TXT record at a catalog zone's
    ///     apex, or a member's <c>&lt;unique-N&gt;.zones.&lt;catalog&gt;</c> PTR record.
    ///     <c>CatalogZoneMembershipService</c> keeps it in sync when a zone's
    ///     <c>MemberOfCatalogZoneId</c> is set or cleared.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Catalog")]
    Catalog = 2,

    /// <summary>
    ///     An A/AAAA record kept in sync by the DHCP-&gt;DNS integration, which registers a DHCP
    ///     client's host name against its leased address. It is created on lease grant, updated if
    ///     the address or host name changes on renewal, and removed on release or expiry. See
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Dhcp")]
    Dhcp = 3,

    /// <summary>
    ///     A TXT record kept in sync by forwarder-zone content sync. It encodes one
    ///     <c>ZoneEntity.ForwarderAddresses</c> entry of a <c>ZoneTypeEnum.Forwarder</c> zone at the
    ///     zone apex, so a <c>ZoneTypeEnum.SecondaryForwarder</c> zone has versioned content to pull
    ///     over AXFR/IXFR. See
    ///     <c>Ninlil.Dns.Server.Authoritative.Forwarding.ForwarderZoneContentSyncService</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ForwarderAddress")]
    ForwarderAddress = 4,
}

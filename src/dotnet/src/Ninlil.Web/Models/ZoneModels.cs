using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries every writable field of a <c>ZoneEntity</c> -- used both to create a
///     new zone (<c>POST /api/zones</c>) and to fully replace an existing one
///     (<c>PUT /api/zones/{id}</c>), since the two share an identical field set. A zone's records
///     (starting with its own SOA) are managed separately, through the nested
///     <c>/api/zones/{zoneId}/records</c> endpoints -- creating a zone here only creates its
///     metadata shell.
/// </summary>
/// <param name="Name">The zone's fully-qualified name, without a trailing dot.</param>
/// <param name="Type">The role this server plays for the zone.</param>
/// <param name="IsDnssecSigned">Whether the zone is signed with DNSSEC.</param>
/// <param name="DnssecAlgorithm">The algorithm used to sign the zone, if signed.</param>
/// <param name="DenialOfExistenceMode">Which denial-of-existence mechanism a signed zone uses.</param>
/// <param name="Nsec3Iterations">How many additional times a signed zone's NSEC3 chain hashes.</param>
/// <param name="Nsec3SaltHex">A signed zone's NSEC3 salt, as hexadecimal text.</param>
/// <param name="IsDisabled">Whether the zone is currently disabled.</param>
/// <param name="PrimaryServerAddresses">
///     The upstream name servers this zone transfers from, for a secondary/stub zone.
/// </param>
/// <param name="ForwarderAddresses">The addresses this zone forwards every query to.</param>
/// <param name="NotifyAddresses">The secondaries this zone sends an outbound NOTIFY to.</param>
/// <param name="TsigKeyName">The TSIG key this zone signs its outbound transfer requests with.</param>
/// <param name="DynamicUpdateEnabled">Whether this zone accepts RFC 2136 dynamic updates.</param>
/// <param name="Comments">Free-form operator notes about the zone.</param>
/// <param name="CloudProviderCredentialId">The credential this zone syncs against, for a cloud zone.</param>
/// <param name="CloudProviderZoneId">This zone's own identifier at its cloud provider.</param>
/// <param name="CloudSyncInterval">How often this zone's cloud sync runs.</param>
/// <param name="AutoManageCaaRecords">
///     Whether the CAA auto-management feature reconciles this zone's CAA records.
///     <see langword="null" /> inherits <c>CaaAutoManagementOptions.DefaultEnabled</c>.
/// </param>
/// <param name="CaaIssuerDomain">
///     This zone's own override of which CA issuer domain CAA auto-management writes
///     into the CAA records it creates. <see langword="null" /> inherits
///     <c>CaaAutoManagementOptions.DefaultIssuerDomain</c>.
/// </param>
/// <param name="AutoManagePtrRecords">
///     Whether the PTR/reverse-zone sync feature mirrors this zone's own A/AAAA records
///     into the enclosing reverse zone. <see langword="null" /> inherits
///     <c>PtrSyncOptions.DefaultEnabled</c> (which itself defaults to <see langword="false" /> --
///     unlike CAA auto-management, this feature is opt-in).
/// </param>
/// <param name="PtrIpv4PrefixLength">
///     This zone's own override of the IPv4 reverse-zone grouping granularity, in bits, or
///     <see langword="null" /> to inherit <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c>.
///     A value of <c>24</c> (the conventional octet boundary) behaves exactly as before RFC 2317
///     support existed; <c>25</c>-<c>30</c> switches to RFC 2317's own classless delegation.
///     Validated at the write boundary to be either <c>null</c> or within <c>24</c>-<c>30</c>
///     inclusive.
/// </param>
/// <param name="PtrIpv6PrefixLength">
///     This zone's own override of the IPv6 reverse-zone grouping granularity, in bits, or
///     <see langword="null" /> to inherit <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>.
///     Validated at the write boundary to be either <c>null</c> or a multiple of <c>4</c> between
///     <c>16</c> and <c>124</c> inclusive.
/// </param>
/// <param name="TransferTransport">
///     Which wire transport carries this zone's own AXFR/IXFR traffic -- see
///     <c>ZoneEntity.TransferTransport</c>'s own doc comment for the full reasoning. Defaults to
///     <see cref="ZoneTransferTransportEnum.Plain" />.
/// </param>
/// <remarks>
///     Deliberately carries no <c>MemberOfCatalogZoneId</c> field: a zone's own RFC 9432 catalog
///     membership is set and cleared only through <c>ZonesController</c>'s own dedicated
///     <c>POST</c>/<c>DELETE /api/zones/{id}/catalog-membership</c> actions, never through this
///     generic create/replace shape -- those actions keep the field and the catalog zone's own
///     member PTR record in sync as one atomic operation, something a plain field write here
///     could not do without duplicating that same logic a second time.
/// </remarks>
public record ZoneWriteRequest(
    string Name,
    ZoneTypeEnum Type,
    bool IsDnssecSigned = false,
    DnssecAlgorithmEnum? DnssecAlgorithm = null,
    DnssecDenialOfExistenceModeEnum DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec3,
    ushort Nsec3Iterations = 10,
    string Nsec3SaltHex = null,
    bool IsDisabled = false,
    IReadOnlyList<string> PrimaryServerAddresses = null,
    IReadOnlyList<string> ForwarderAddresses = null,
    IReadOnlyList<string> NotifyAddresses = null,
    string TsigKeyName = null,
    bool DynamicUpdateEnabled = false,
    string Comments = null,
    Guid? CloudProviderCredentialId = null,
    string CloudProviderZoneId = null,
    TimeSpan? CloudSyncInterval = null,
    bool? AutoManageCaaRecords = null,
    string CaaIssuerDomain = null,
    bool? AutoManagePtrRecords = null,
    int? PtrIpv4PrefixLength = null,
    int? PtrIpv6PrefixLength = null,
    ZoneTransferTransportEnum TransferTransport = ZoneTransferTransportEnum.Plain
);

/// <summary>
///     This class carries a zone's full state, for both list and single-item responses.
/// </summary>
/// <param name="Id">The zone's own identifier.</param>
/// <param name="Name">The zone's fully-qualified name, without a trailing dot.</param>
/// <param name="Type">The role this server plays for the zone.</param>
/// <param name="IsDnssecSigned">Whether the zone is signed with DNSSEC.</param>
/// <param name="DnssecAlgorithm">The algorithm used to sign the zone, if signed.</param>
/// <param name="DenialOfExistenceMode">Which denial-of-existence mechanism a signed zone uses.</param>
/// <param name="Nsec3Iterations">How many additional times a signed zone's NSEC3 chain hashes.</param>
/// <param name="Nsec3SaltHex">A signed zone's NSEC3 salt, as hexadecimal text.</param>
/// <param name="IsDisabled">Whether the zone is currently disabled.</param>
/// <param name="PrimaryServerAddresses">
///     The upstream name servers this zone transfers from, for a secondary/stub zone.
/// </param>
/// <param name="ForwarderAddresses">The addresses this zone forwards every query to.</param>
/// <param name="NotifyAddresses">The secondaries this zone sends an outbound NOTIFY to.</param>
/// <param name="TsigKeyName">The TSIG key this zone signs its outbound transfer requests with.</param>
/// <param name="DynamicUpdateEnabled">Whether this zone accepts RFC 2136 dynamic updates.</param>
/// <param name="Comments">Free-form operator notes about the zone.</param>
/// <param name="CloudProviderCredentialId">The credential this zone syncs against, for a cloud zone.</param>
/// <param name="CloudProviderZoneId">This zone's own identifier at its cloud provider.</param>
/// <param name="CloudSyncInterval">How often this zone's cloud sync runs.</param>
/// <param name="AutoManageCaaRecords">
///     Whether the CAA auto-management feature reconciles this zone's CAA records.
///     <see langword="null" /> inherits <c>CaaAutoManagementOptions.DefaultEnabled</c>.
/// </param>
/// <param name="CaaIssuerDomain">
///     This zone's own override of which CA issuer domain CAA auto-management writes
///     into the CAA records it creates. <see langword="null" /> inherits
///     <c>CaaAutoManagementOptions.DefaultIssuerDomain</c>.
/// </param>
/// <param name="AutoManagePtrRecords">
///     Whether the PTR/reverse-zone sync feature mirrors this zone's own A/AAAA records
///     into the enclosing reverse zone. <see langword="null" /> inherits
///     <c>PtrSyncOptions.DefaultEnabled</c>.
/// </param>
/// <param name="PtrIpv4PrefixLength">
///     This zone's own override of the IPv4 reverse-zone grouping granularity, in bits.
///     <see langword="null" /> inherits <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c>.
/// </param>
/// <param name="PtrIpv6PrefixLength">
///     This zone's own override of the IPv6 reverse-zone grouping granularity, in bits.
///     <see langword="null" /> inherits <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>.
/// </param>
/// <param name="CreatedAtUtc">When the zone was created.</param>
/// <param name="UpdatedAtUtc">When the zone was last updated, if it ever has been.</param>
/// <param name="TransferTransport">Which wire transport carries this zone's own AXFR/IXFR traffic.</param>
/// <param name="MemberOfCatalogZoneId">
///     The identifier of the RFC 9432 catalog zone this zone is a member of, or
///     <see langword="null" /> if it participates in none. Read-only here -- set or cleared only
///     through <c>POST</c>/<c>DELETE /api/zones/{id}/catalog-membership</c>, never through this
///     record's own create/replace/patch counterparts.
/// </param>
public record ZoneDetail(
    Guid Id,
    string Name,
    ZoneTypeEnum Type,
    bool IsDnssecSigned,
    DnssecAlgorithmEnum? DnssecAlgorithm,
    DnssecDenialOfExistenceModeEnum DenialOfExistenceMode,
    ushort Nsec3Iterations,
    string Nsec3SaltHex,
    bool IsDisabled,
    IReadOnlyList<string> PrimaryServerAddresses,
    IReadOnlyList<string> ForwarderAddresses,
    IReadOnlyList<string> NotifyAddresses,
    string TsigKeyName,
    bool DynamicUpdateEnabled,
    string Comments,
    Guid? CloudProviderCredentialId,
    string CloudProviderZoneId,
    TimeSpan CloudSyncInterval,
    bool? AutoManageCaaRecords,
    string CaaIssuerDomain,
    bool? AutoManagePtrRecords,
    int? PtrIpv4PrefixLength,
    int? PtrIpv6PrefixLength,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    ZoneTransferTransportEnum TransferTransport,
    Guid? MemberOfCatalogZoneId
);

/// <summary>
///     This record is <c>POST /api/zones/{id}/reconcile-caa</c>'s own response body -- what one
///     on-demand CAA reconciliation pass actually did, or why it did nothing at all. A thin REST
///     wrapper over <c>Server.Acme.Caa.CaaReconciliationResult</c>, following this project's own
///     "never expose an internal service-layer result type directly over REST" convention (the
///     same reasoning <c>ZoneCatalogMembersResponse</c> already establishes for
///     <c>CatalogZoneMembershipService.ListMembersAsync</c>'s own return value).
/// </summary>
/// <param name="RecordsAdded">How many CAA records this pass created.</param>
/// <param name="RecordsRemoved">How many CAA records this pass removed as no longer desired.</param>
/// <param name="Skipped">
///     Whether this pass did no work at all (see <paramref name="SkipReason" />) -- distinct from
///     a pass that ran but found nothing to change (reported as <see langword="false" /> here with
///     zero counts).
/// </param>
/// <param name="SkipReason">Why this pass was skipped, populated only when <paramref name="Skipped" /> is <see langword="true" />.</param>
public record ZoneCaaReconciliationResponse(
    int RecordsAdded,
    int RecordsRemoved,
    bool Skipped,
    string SkipReason
);

/// <summary>
///     This record is <c>POST /api/zones/{id}/reconcile-ptr</c>'s own response body -- what one
///     on-demand PTR/reverse-zone sync reconciliation pass actually did, or why it did nothing at
///     all. A thin REST wrapper over <c>Server.Authoritative.Ptr.PtrReconciliationResult</c>, the
///     same shape <see cref="ZoneCaaReconciliationResponse" /> uses for its own equivalent result.
/// </summary>
/// <param name="RecordsAdded">
///     How many PTR/delegation-CNAME records this pass created, across every reverse zone and
///     (for a classless IPv4 grouping) every RFC 2317 delegating parent zone touched.
/// </param>
/// <param name="RecordsRemoved">The same, for records removed as no longer desired.</param>
/// <param name="ReverseZonesTouched">
///     How many distinct zones this pass created, wrote into, or visited for stale-record
///     cleanup.
/// </param>
/// <param name="Skipped">
///     Whether this pass did no work at all (see <paramref name="SkipReason" />) -- distinct from
///     a pass that ran but found nothing to change (reported as <see langword="false" /> here with
///     zero counts).
/// </param>
/// <param name="SkipReason">Why this pass was skipped, populated only when <paramref name="Skipped" /> is <see langword="true" />.</param>
public record ZonePtrReconciliationResponse(
    int RecordsAdded,
    int RecordsRemoved,
    int ReverseZonesTouched,
    bool Skipped,
    string SkipReason
);

/// <summary>
///     This class carries the catalog zone a caller wants a zone to join --
///     <c>POST /api/zones/{id}/catalog-membership</c>'s own request body.
/// </summary>
/// <param name="CatalogZoneId">The identifier of the <see cref="ZoneTypeEnum.Catalog" /> zone to join.</param>
public record ZoneCatalogMembershipRequest(Guid CatalogZoneId);

/// <summary>
///     This class carries a single catalog zone's own currently-enumerated member zone names --
///     <c>GET /api/zones/{id}/catalog-members</c>'s own response body.
/// </summary>
/// <param name="MemberZoneNames">Every member zone name the catalog currently lists, alphabetically.</param>
public record ZoneCatalogMembersResponse(IReadOnlyList<string> MemberZoneNames);

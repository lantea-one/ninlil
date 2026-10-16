using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries every writable field of a <c>ZoneEntity</c>, used to create a zone
///     (<c>POST /api/zones</c>) and to fully replace one (<c>PUT /api/zones/{id}</c>). Creating a
///     zone adds no records, not even an SOA; records are managed through
///     <c>/api/zones/{zoneId}/records</c>.
/// </summary>
/// <param name="Name">The zone's fully-qualified name, without a trailing dot.</param>
/// <param name="Type">The role this server plays for the zone.</param>
/// <param name="IsDnssecSigned">Whether the zone is signed with DNSSEC.</param>
/// <param name="DnssecAlgorithm">The algorithm used to sign the zone, if signed.</param>
/// <param name="DenialOfExistenceMode">Which denial-of-existence mechanism a signed zone uses.</param>
/// <param name="Nsec3Iterations">How many additional times a signed zone's NSEC3 chain hashes. Defaults to 0 (RFC 9276 §3.1); at most 150, because validators may treat a higher count as insecure.</param>
/// <param name="Nsec3SaltHex">A signed zone's NSEC3 salt, as hexadecimal text. Leave it empty: RFC 9276 §3.1 advises against a salt.</param>
/// <param name="IsDisabled">Whether the zone is disabled.</param>
/// <param name="PrimaryServerAddresses">
///     The name servers a secondary or stub zone transfers from.
/// </param>
/// <param name="ForwarderAddresses">The addresses a forwarder zone sends every query to.</param>
/// <param name="NotifyAddresses">The secondaries the zone sends NOTIFY messages to.</param>
/// <param name="TsigKeyName">The TSIG key the zone signs its transfer requests with.</param>
/// <param name="DynamicUpdateEnabled">
///     Whether the zone accepts RFC 2136 dynamic updates. Applies to Primary and CloudPrimary
///     zones. A CloudPrimary zone pushes an accepted update to its provider within moments and
///     refuses an update that adds a record type the providers cannot hold (anything but A, AAAA,
///     CNAME, MX, NS, TXT, SRV and CAA).
/// </param>
/// <param name="UpdateTsigKeyNames">
///     The TSIG keys allowed to send dynamic updates to the zone (lowercase, no trailing dot). An
///     empty list accepts no key.
/// </param>
/// <param name="TransferTsigKeyNames">
///     The TSIG keys allowed to transfer the zone (AXFR and IXFR). An empty list accepts no key.
/// </param>
/// <param name="Comments">Free-form operator notes about the zone.</param>
/// <param name="CloudProviderCredentialId">The credential a cloud zone syncs with.</param>
/// <param name="CloudProviderZoneId">The zone's identifier at its cloud provider.</param>
/// <param name="CloudSyncInterval">How often a cloud zone is synced.</param>
/// <param name="AutoManageCaaRecords">
///     Whether CAA auto-management keeps the zone's CAA records in step.
///     <see langword="null" /> inherits <c>CaaAutoManagementOptions.DefaultEnabled</c>.
/// </param>
/// <param name="CaaIssuerDomain">
///     The CA issuer domain CAA auto-management writes into the zone's CAA records.
///     <see langword="null" /> inherits <c>CaaAutoManagementOptions.DefaultIssuerDomain</c>.
/// </param>
/// <param name="AutoManagePtrRecords">
///     Whether PTR sync mirrors the zone's A and AAAA records into the matching reverse zone.
///     <see langword="null" /> inherits <c>PtrSyncOptions.DefaultEnabled</c>, which defaults to
///     <see langword="false" />: unlike CAA auto-management, PTR sync is opt-in.
/// </param>
/// <param name="PtrIpv4PrefixLength">
///     The zone's IPv4 reverse zone size, in prefix bits, or <see langword="null" /> to inherit
///     <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c>. 24 means one zone per octet
///     boundary; 25 to 30 use RFC 2317 classless delegation. Only <c>null</c> or 24 to 30 is
///     accepted.
/// </param>
/// <param name="PtrIpv6PrefixLength">
///     The zone's IPv6 reverse zone size, in prefix bits, or <see langword="null" /> to inherit
///     <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>. Only <c>null</c> or a multiple of 4
///     from 16 to 124 is accepted.
/// </param>
/// <param name="TransferTransport">
///     The transport that carries the zone's AXFR and IXFR traffic (see
///     <c>ZoneEntity.TransferTransport</c>). Defaults to <see cref="ZoneTransferTransportEnum.Plain" />.
/// </param>
/// <param name="ValidateDnssec">
///     Whether answers forwarded for a forwarder zone are validated with DNSSEC. Off by default,
///     because a private zone has no chain of trust from the public root. The global DNSSEC
///     validation setting must also be on, and names on the Insecure domain list are never
///     validated.
/// </param>
/// <remarks>
///     There is no <c>MemberOfCatalogZoneId</c> field. RFC 9432 catalog membership is set and
///     cleared only through <c>POST</c> and <c>DELETE /api/zones/{id}/catalog-membership</c>,
///     which update the field and the catalog zone's member PTR record together.
/// </remarks>
public record ZoneWriteRequest(
    string Name,
    ZoneTypeEnum Type,
    bool IsDnssecSigned = false,
    DnssecAlgorithmEnum? DnssecAlgorithm = null,
    DnssecDenialOfExistenceModeEnum DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec3,
    ushort Nsec3Iterations = 0,
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
    ZoneTransferTransportEnum TransferTransport = ZoneTransferTransportEnum.Plain,
    IReadOnlyList<string> UpdateTsigKeyNames = null,
    IReadOnlyList<string> TransferTsigKeyNames = null,
    bool ValidateDnssec = false
);

/// <summary>
///     This class carries a zone, as returned by the list, get and write endpoints of
///     <c>/api/zones</c>. The fields mean what they mean in <see cref="ZoneWriteRequest" />.
/// </summary>
/// <param name="Id">The zone's identifier.</param>
/// <param name="Name">The zone's fully-qualified name, without a trailing dot.</param>
/// <param name="Type">The role this server plays for the zone.</param>
/// <param name="IsDnssecSigned">Whether the zone is signed with DNSSEC.</param>
/// <param name="DnssecAlgorithm">The algorithm used to sign the zone, if signed. An algorithm prohibited for signing (RSAMD5, DSA, RSASHA1, DSA-NSEC3-SHA1, RSASHA1-NSEC3-SHA1, ECC-GOST) is rejected with 400.</param>
/// <param name="DenialOfExistenceMode">Which denial-of-existence mechanism a signed zone uses.</param>
/// <param name="Nsec3Iterations">How many additional times a signed zone's NSEC3 chain hashes. A new zone starts at 0 (RFC 9276 §3.1); a zone saved earlier keeps its count.</param>
/// <param name="Nsec3SaltHex">A signed zone's NSEC3 salt, as hexadecimal text; empty or null means no salt.</param>
/// <param name="IsDisabled">Whether the zone is disabled.</param>
/// <param name="PrimaryServerAddresses">The name servers a secondary or stub zone transfers from.</param>
/// <param name="ForwarderAddresses">The addresses a forwarder zone sends every query to.</param>
/// <param name="NotifyAddresses">The secondaries the zone sends NOTIFY messages to.</param>
/// <param name="TsigKeyName">The TSIG key the zone signs its transfer requests with.</param>
/// <param name="DynamicUpdateEnabled">
///     Whether the zone accepts RFC 2136 dynamic updates. Applies to Primary and CloudPrimary
///     zones. A CloudPrimary zone pushes an accepted update to its provider within moments and
///     refuses an update that adds a record type the providers cannot hold (anything but A, AAAA,
///     CNAME, MX, NS, TXT, SRV and CAA).
/// </param>
/// <param name="UpdateTsigKeyNames">
///     The TSIG keys allowed to send dynamic updates to the zone (lowercase, no trailing dot). An
///     empty list accepts no key.
/// </param>
/// <param name="TransferTsigKeyNames">
///     The TSIG keys allowed to transfer the zone (AXFR and IXFR). An empty list accepts no key.
/// </param>
/// <param name="Comments">Free-form operator notes about the zone.</param>
/// <param name="CloudProviderCredentialId">The credential a cloud zone syncs with.</param>
/// <param name="CloudProviderZoneId">The zone's identifier at its cloud provider.</param>
/// <param name="CloudSyncInterval">How often a cloud zone is synced.</param>
/// <param name="AutoManageCaaRecords">
///     Whether CAA auto-management keeps the zone's CAA records in step.
///     <see langword="null" /> inherits <c>CaaAutoManagementOptions.DefaultEnabled</c>.
/// </param>
/// <param name="CaaIssuerDomain">
///     The CA issuer domain CAA auto-management writes. <see langword="null" /> inherits
///     <c>CaaAutoManagementOptions.DefaultIssuerDomain</c>.
/// </param>
/// <param name="AutoManagePtrRecords">
///     Whether PTR sync mirrors the zone's A and AAAA records into the matching reverse zone.
///     <see langword="null" /> inherits <c>PtrSyncOptions.DefaultEnabled</c>.
/// </param>
/// <param name="PtrIpv4PrefixLength">
///     The zone's IPv4 reverse zone size, in prefix bits. <see langword="null" /> inherits
///     <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c>.
/// </param>
/// <param name="PtrIpv6PrefixLength">
///     The zone's IPv6 reverse zone size, in prefix bits. <see langword="null" /> inherits
///     <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>.
/// </param>
/// <param name="CreatedAtUtc">When the zone was created.</param>
/// <param name="UpdatedAtUtc">When the zone was last updated, or <see langword="null" /> if never.</param>
/// <param name="TransferTransport">The transport that carries the zone's AXFR and IXFR traffic.</param>
/// <param name="MemberOfCatalogZoneId">
///     The RFC 9432 catalog zone this zone belongs to, or <see langword="null" /> if none.
///     Read-only: it changes only through <c>POST</c> and
///     <c>DELETE /api/zones/{id}/catalog-membership</c>.
/// </param>
/// <param name="ValidateDnssec">
///     Whether answers forwarded for a forwarder zone are validated with DNSSEC. Off by default,
///     because a private zone has no chain of trust from the public root. The global DNSSEC
///     validation setting must also be on, and names on the Insecure domain list are never
///     validated.
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
    IReadOnlyList<string> UpdateTsigKeyNames,
    IReadOnlyList<string> TransferTsigKeyNames,
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
    Guid? MemberOfCatalogZoneId,
    bool ValidateDnssec
);

/// <summary>
///     This record is the response of <c>POST /api/zones/{id}/reconcile-caa</c>: what one
///     on-demand CAA reconciliation did, or why it did nothing. It copies
///     <c>Server.Acme.Caa.CaaReconciliationResult</c> so the internal service type is not exposed
///     over REST, as <see cref="ZoneCatalogMembersResponse" /> does for the catalog service.
/// </summary>
/// <param name="RecordsAdded">How many CAA records the pass created.</param>
/// <param name="RecordsRemoved">How many CAA records the pass removed as no longer wanted.</param>
/// <param name="Skipped">
///     Whether the pass did nothing (see <paramref name="SkipReason" />). A pass that ran but
///     found nothing to change reports <see langword="false" /> with zero counts.
/// </param>
/// <param name="SkipReason">Why the pass was skipped, set only when <paramref name="Skipped" /> is <see langword="true" />.</param>
public record ZoneCaaReconciliationResponse(
    int RecordsAdded,
    int RecordsRemoved,
    bool Skipped,
    string SkipReason
);

/// <summary>
///     This record is the response of <c>POST /api/zones/{id}/reconcile-ptr</c>: what one
///     on-demand PTR sync did, or why it did nothing. It copies
///     <c>Server.Authoritative.Ptr.PtrReconciliationResult</c>, as
///     <see cref="ZoneCaaReconciliationResponse" /> does for CAA.
/// </summary>
/// <param name="RecordsAdded">
///     How many PTR and delegation CNAME records the pass created, across every reverse zone and,
///     for a classless IPv4 grouping, every RFC 2317 delegating parent zone.
/// </param>
/// <param name="RecordsRemoved">How many such records the pass removed as no longer wanted.</param>
/// <param name="ReverseZonesTouched">
///     How many distinct zones the pass created, wrote to or checked for stale records.
/// </param>
/// <param name="Skipped">
///     Whether the pass did nothing (see <paramref name="SkipReason" />). A pass that ran but
///     found nothing to change reports <see langword="false" /> with zero counts.
/// </param>
/// <param name="SkipReason">Why the pass was skipped, set only when <paramref name="Skipped" /> is <see langword="true" />.</param>
public record ZonePtrReconciliationResponse(
    int RecordsAdded,
    int RecordsRemoved,
    int ReverseZonesTouched,
    bool Skipped,
    string SkipReason
);

/// <summary>
///     This class carries the catalog zone a zone should join, the request body of
///     <c>POST /api/zones/{id}/catalog-membership</c>.
/// </summary>
/// <param name="CatalogZoneId">The identifier of the <see cref="ZoneTypeEnum.Catalog" /> zone to join.</param>
public record ZoneCatalogMembershipRequest(Guid CatalogZoneId);

/// <summary>
///     This class carries the member zone names of one catalog zone, the response of
///     <c>GET /api/zones/{id}/catalog-members</c>.
/// </summary>
/// <param name="MemberZoneNames">Every member zone name the catalog lists, in alphabetical order.</param>
public record ZoneCatalogMembersResponse(IReadOnlyList<string> MemberZoneNames);

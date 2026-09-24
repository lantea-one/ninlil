using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DNS zone hosted or referenced by this server -- the unit of
///     administrative authority the DNS delegates, such as <c>example.com</c>.
/// </summary>
public class ZoneEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the zone's fully-qualified name, stored without a trailing dot.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the role this server plays for the zone.
    /// </summary>
    public ZoneTypeEnum Type { get; set; }

    /// <summary>
    ///     This property indicates whether the zone is signed with DNSSEC.
    /// </summary>
    public bool IsDnssecSigned { get; set; }

    /// <summary>
    ///     This property contains the algorithm used to sign the zone, or <see langword="null" />
    ///     if <see cref="IsDnssecSigned" /> is <see langword="false" />.
    /// </summary>
    public DnssecAlgorithmEnum? DnssecAlgorithm { get; set; }

    /// <summary>
    ///     This property controls which denial-of-existence mechanism a signed zone uses -- both
    ///     are supported rather than only one. Meaningless while <see cref="IsDnssecSigned" /> is
    ///     <see langword="false" />.
    /// </summary>
    public DnssecDenialOfExistenceModeEnum DenialOfExistenceMode { get; set; } =
        DnssecDenialOfExistenceModeEnum.Nsec3;

    /// <summary>
    ///     This property contains how many additional times a signed zone's
    ///     <see cref="Nsec3RecordData" /> chain applies its hash, when
    ///     <see cref="DenialOfExistenceMode" /> is <see cref="DnssecDenialOfExistenceModeEnum.Nsec3" />.
    ///     RFC 5155 recommends keeping this low -- a high iteration count raises validator CPU
    ///     cost more than it meaningfully raises the cost of the dictionary attack it defends
    ///     against.
    /// </summary>
    public ushort Nsec3Iterations { get; set; } = 10;

    /// <summary>
    ///     This property contains the salt a signed zone's <see cref="Nsec3RecordData" />
    ///     chain mixes into every hash, as a hexadecimal string (<see langword="null" /> or empty
    ///     means no salt at all, a valid RFC 5155 configuration). Kept as hex text rather than
    ///     raw bytes since, unlike <see cref="DnssecKeyEntity.PrivateKeyPkcs8Encrypted" />, a
    ///     zone's salt is not a secret -- it is published in the zone's own NSEC3PARAM record --
    ///     so there is no need for a binary column at all.
    /// </summary>
    public string Nsec3SaltHex { get; set; }

    /// <summary>
    ///     This property contains whether the zone is currently disabled. A disabled zone keeps
    ///     its configuration and records but stops answering queries.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the addresses of the upstream name servers this zone transfers
    ///     from, when <see cref="Type" /> is <see cref="ZoneTypeEnum.Secondary" />,
    ///     <see cref="ZoneTypeEnum.Stub" />, or <see cref="ZoneTypeEnum.SecondaryCatalog" />.
    /// </summary>
    public List<string> PrimaryServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the addresses this zone forwards every query to, when
    ///     <see cref="Type" /> is <see cref="ZoneTypeEnum.Forwarder" /> or
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" />.
    /// </summary>
    public List<string> ForwarderAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the addresses of the secondary servers this server, when
    ///     acting as <see cref="ZoneTypeEnum.Primary" /> for this zone, sends an RFC 1996 NOTIFY
    ///     to whenever the zone changes. This is an explicit, operator-maintained list rather
    ///     than something inferred from the zone's own NS records -- inferring it would risk
    ///     silently notifying (and effectively fingerprinting a zone's data to) a name server
    ///     nobody actually intended to receive it, just because it happens to be listed as
    ///     authoritative.
    /// </summary>
    public List<string> NotifyAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the name of the pre-shared TSIG key this server signs its own
    ///     outbound SOA-refresh queries and AXFR/IXFR pull requests with, when <see cref="Type" />
    ///     is <see cref="ZoneTypeEnum.Secondary" /> and the primary requires one -- the mirror
    ///     image of a primary requiring TSIG on the transfers it serves out. <see langword="null" />
    ///     when the primary accepts unsigned requests.
    /// </summary>
    public string TsigKeyName { get; set; }

    /// <summary>
    ///     This property controls whether this zone accepts RFC 2136 dynamic updates at all,
    ///     when <see cref="Type" /> is <see cref="ZoneTypeEnum.Primary" />. Off by default -- a
    ///     zone must opt in explicitly before anything can modify it over the wire, even with a
    ///     valid TSIG signature.
    /// </summary>
    public bool DynamicUpdateEnabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the zone.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property contains the credential set this zone syncs against, when
    ///     <see cref="Type" /> is <see cref="ZoneTypeEnum.CloudPrimary" /> or
    ///     <see cref="ZoneTypeEnum.CloudSecondary" />. <see langword="null" /> otherwise.
    /// </summary>
    public Guid? CloudProviderCredentialId { get; set; }

    /// <summary>
    ///     This property contains this zone's own identifier at the cloud provider named by
    ///     <see cref="CloudProviderCredentialId" /> -- a Cloudflare zone ID, a Route 53 hosted
    ///     zone ID, or a DigitalOcean domain name, depending on that credential's own
    ///     <see cref="CloudDnsProviderCredentialEntity.ProviderType" />. <see langword="null" />
    ///     unless <see cref="Type" /> is <see cref="ZoneTypeEnum.CloudPrimary" /> or
    ///     <see cref="ZoneTypeEnum.CloudSecondary" />.
    /// </summary>
    public string CloudProviderZoneId { get; set; }

    /// <summary>
    ///     This property contains how often <c>CloudDnsZoneSyncService</c> pushes (for
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />) or pulls (for
    ///     <see cref="ZoneTypeEnum.CloudSecondary" />) this zone against its cloud provider --
    ///     the REST-polling analogue of an RFC 5936 secondary's own refresh interval, since a
    ///     REST API has no RFC 1996 NOTIFY equivalent to push a change proactively.
    /// </summary>
    public TimeSpan CloudSyncInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property controls whether the CAA auto-management feature reconciles this
    ///     zone's CAA record set whenever its ACME enrollment or CA selection changes.
    ///     <see langword="null" /> (the default for a newly-created zone) means "inherit whatever
    ///     <c>CaaAutoManagementOptions.DefaultEnabled</c> currently says" rather than a hardcoded
    ///     value baked into this entity -- the same "nullable per-zone override, global default
    ///     when unset" shape this property's own sibling, <see cref="CaaIssuerDomain" />, also
    ///     uses. Only meaningful for <see cref="ZoneTypeEnum.Primary" />/
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> zones
    ///     -- a zone this server does not own the content of has no CAA record set of its own to
    ///     reconcile.
    /// </summary>
    public bool? AutoManageCaaRecords { get; set; }

    /// <summary>
    ///     This property contains this zone's own override of which CA's issuer domain the
    ///     CAA auto-management feature writes into the CAA records it creates (for example,
    ///     <c>letsencrypt.org</c>), or <see langword="null" /> to inherit
    ///     <c>CaaAutoManagementOptions.DefaultIssuerDomain</c>. Deliberately a new, explicit
    ///     setting rather than something derived from <c>AcmeOptions.DirectoryUri</c> -- a
    ///     URL-parsing heuristic could guess the wrong registrable domain for a CA with an
    ///     unusual hostname shape, and a wrong CAA <c>issue</c> value does not just log a
    ///     warning, it can outright break real certificate issuance for this zone.
    /// </summary>
    public string CaaIssuerDomain { get; set; }

    /// <summary>
    ///     This property controls whether the PTR/reverse-zone sync feature mirrors this
    ///     zone's own A/AAAA record set into the enclosing <c>in-addr.arpa</c>/<c>ip6.arpa</c>
    ///     reverse zone. <see langword="null" /> (the default for a newly-created zone) means
    ///     "inherit whatever <c>PtrSyncOptions.DefaultEnabled</c> currently says" -- the same
    ///     "nullable per-zone override, global default when unset" shape
    ///     <see cref="AutoManageCaaRecords" /> already uses. Unlike that field, this feature's own
    ///     global default is <see langword="false" /> (an explicit opt-in), not
    ///     <see langword="true" /> -- creating a brand-new zone (the reverse zone itself) and
    ///     writing records into it is a larger, more surprising side effect for an operator who
    ///     did not ask for it than reconciling CAA records on a zone whose ACME enrollment was
    ///     already an explicit opt-in of its own. Only meaningful for
    ///     <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" /> zones --
    ///     a zone this server does not own the content of has no A/AAAA record set of its own to
    ///     mirror.
    /// </summary>
    public bool? AutoManagePtrRecords { get; set; }

    /// <summary>
    ///     This property contains this zone's own override of the IPv4 reverse-zone grouping
    ///     granularity, in bits, the PTR/reverse-zone sync feature uses for this zone's own
    ///     A-record addresses, or <see langword="null" /> to inherit
    ///     <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c>. The same "nullable per-zone
    ///     override, global default when unset" shape <see cref="AutoManagePtrRecords" /> already
    ///     uses. A value of <c>24</c> (the conventional octet boundary, and the compiled-in
    ///     default both this field and the options-level default ultimately fall back to)
    ///     reproduces this feature's original, pre-RFC-2317 behavior; a value from <c>25</c>
    ///     through <c>30</c> switches this zone's own reverse-zone(s) to RFC 2317's own classless
    ///     <c>"{start}/{prefix}"</c> naming convention, with the octet-aligned <c>/24</c> parent
    ///     zone automatically gaining the CNAME delegation glue RFC 2317 §4 describes -- see
    ///     <c>Server.Authoritative.Ptr.ReverseZoneNameCalculator</c> for the full mechanism. Only
    ///     meaningful for <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" />
    ///     zones, the same eligibility <see cref="AutoManagePtrRecords" /> already restricts itself
    ///     to.
    /// </summary>
    public int? PtrIpv4PrefixLength { get; set; }

    /// <summary>
    ///     This property contains this zone's own override of the IPv6 reverse-zone grouping
    ///     granularity, in bits, the PTR/reverse-zone sync feature uses for this zone's own
    ///     AAAA-record addresses, or <see langword="null" /> to inherit
    ///     <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>. The same "nullable per-zone
    ///     override, global default when unset" shape <see cref="PtrIpv4PrefixLength" /> uses --
    ///     must be a multiple of <c>4</c> (every <c>ip6.arpa</c> label is exactly one hex nibble,
    ///     per RFC 3596), validated at the REST write boundary. Only meaningful for
    ///     <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" /> zones, the
    ///     same eligibility <see cref="AutoManagePtrRecords" /> already restricts itself to.
    /// </summary>
    public int? PtrIpv6PrefixLength { get; set; }

    /// <summary>
    ///     This property controls which wire transport carries this zone's own AXFR/IXFR traffic.
    ///     For a <see cref="ZoneTypeEnum.Secondary" />/<see cref="ZoneTypeEnum.Stub" />/
    ///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone, this is the transport
    ///     <c>SecondaryZoneRefreshService</c> connects with when pulling from
    ///     <see cref="PrimaryServerAddresses" />. Defaults to <see cref="ZoneTransferTransportEnum.Plain" />
    ///     so every zone created before this feature existed keeps its exact current behavior.
    ///     TSIG (<see cref="TsigKeyName" />) remains the actual authentication layer regardless of
    ///     this setting -- RFC 9103/9250 transport encryption only adds confidentiality on top, it
    ///     never replaces TSIG. On the serving side (this server acting as
    ///     <see cref="ZoneTypeEnum.Primary" />), this field has no effect: an inbound AXFR/IXFR
    ///     request is always served over whichever transport it actually arrived on (plain TCP,
    ///     DNS-over-TLS, or DNS-over-QUIC), each still gated by TSIG verification exactly as before
    ///     -- there is deliberately no separate "require encrypted transfer-out" enforcement here,
    ///     since that would need a REST-layer check this project's own zone-type-eligibility
    ///     precedent (<see cref="AutoManageCaaRecords" />) shows is easy to get wrong quietly;
    ///     simply not offering unencrypted transports to a secondary is a real, if softer,
    ///     enforcement an operator has full control over from the secondary's own side.
    /// </summary>
    public ZoneTransferTransportEnum TransferTransport { get; set; } =
        ZoneTransferTransportEnum.Plain;

    /// <summary>
    ///     This property contains the identifier of the <see cref="ZoneTypeEnum.Catalog" /> zone
    ///     this zone is a member of, or <see langword="null" /> if it participates in none.
    ///     Meaningful only while <see cref="Type" /> is <see cref="ZoneTypeEnum.Primary" /> or
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> on the node that authors this zone -- the same
    ///     zone-type eligibility <see cref="AutoManageCaaRecords" />/<see cref="AutoManagePtrRecords" />
    ///     already restrict themselves to, since only a zone this server owns the content of has
    ///     anything of its own to advertise into a catalog.
    ///     <para>
    ///         This field also does double duty marking a zone auto-provisioned by the
    ///         <see cref="ZoneTypeEnum.SecondaryCatalog" /> consumer side of the same mechanism: a
    ///         zone this server never heard of until a catalog zone's own member list mentioned it
    ///         is created locally as <see cref="ZoneTypeEnum.Secondary" /> with this field set to
    ///         the catalog zone's own identifier, so a later catalog update can tell "a zone this
    ///         auto-provisioning mechanism itself created, safe to remove if the catalog stops
    ///         listing it" apart from an operator's own, unrelated zone that merely happens to
    ///         share a name -- the exact same provenance-marker role
    ///         <see cref="Data.Abstractions.Entity.RecordEntity.ManagedByFeature" /> already plays
    ///         for individual records.
    ///     </para>
    /// </summary>
    public Guid? MemberOfCatalogZoneId { get; set; }
}

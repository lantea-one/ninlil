using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a DNS zone hosted or referenced by this server: a unit of delegated
///     authority such as <c>example.com</c>. <see cref="Type" /> decides which of the other
///     properties apply; its records are <see cref="RecordEntity" /> rows and its DNSSEC keys are
///     <see cref="DnssecKeyEntity" /> rows.
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
    ///     This property controls which denial-of-existence mechanism a signed zone uses. It has no
    ///     effect while <see cref="IsDnssecSigned" /> is <see langword="false" />.
    /// </summary>
    public DnssecDenialOfExistenceModeEnum DenialOfExistenceMode { get; set; } =
        DnssecDenialOfExistenceModeEnum.Nsec3;

    /// <summary>
    ///     This property contains how many additional times a signed zone's
    ///     <see cref="Nsec3RecordData" /> chain applies its hash, when
    ///     <see cref="DenialOfExistenceMode" /> is <see cref="DnssecDenialOfExistenceModeEnum.Nsec3" />.
    ///     Keep it low: a high count costs validators more CPU than it costs a dictionary attacker
    ///     (see RFC 9276).
    /// </summary>
    public ushort Nsec3Iterations { get; set; } = 10;

    /// <summary>
    ///     This property contains the salt a signed zone's <see cref="Nsec3RecordData" /> chain mixes
    ///     into every hash, as a hexadecimal string (<see langword="null" /> or empty means no salt,
    ///     which RFC 5155 allows). It is hex text rather than bytes because, unlike
    ///     <see cref="DnssecKeyEntity.PrivateKeyPkcs8Encrypted" />, it is not secret: the zone's
    ///     NSEC3PARAM record publishes it.
    /// </summary>
    public string Nsec3SaltHex { get; set; }

    /// <summary>
    ///     This property indicates whether the zone is disabled. A disabled zone keeps its
    ///     configuration and records but does not answer queries.
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
    ///     This property contains the addresses of the secondary servers that get an RFC 1996 NOTIFY
    ///     when this <see cref="ZoneTypeEnum.Primary" /> zone changes. It is an explicit list rather
    ///     than taken from the zone's NS records, so no server is notified just because it is
    ///     listed as authoritative.
    /// </summary>
    public List<string> NotifyAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the name of the TSIG key that signs this server's SOA refresh
    ///     queries and AXFR/IXFR requests, when <see cref="Type" /> is
    ///     <see cref="ZoneTypeEnum.Secondary" /> and the primary requires TSIG.
    ///     <see langword="null" /> when the primary accepts unsigned requests.
    /// </summary>
    public string TsigKeyName { get; set; }

    /// <summary>
    ///     This property controls whether this zone accepts RFC 2136 dynamic updates, when
    ///     <see cref="Type" /> is <see cref="ZoneTypeEnum.Primary" />. It is off by default: a zone
    ///     must opt in before anything can change it over the wire, even with a valid TSIG
    ///     signature.
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
    ///     This property contains this zone's identifier at the cloud provider of
    ///     <see cref="CloudProviderCredentialId" />: a Cloudflare zone ID, a Route 53 hosted zone ID
    ///     or a DigitalOcean domain name, depending on the credential's
    ///     <see cref="CloudDnsProviderCredentialEntity.ProviderType" />. <see langword="null" />
    ///     unless <see cref="Type" /> is <see cref="ZoneTypeEnum.CloudPrimary" /> or
    ///     <see cref="ZoneTypeEnum.CloudSecondary" />.
    /// </summary>
    public string CloudProviderZoneId { get; set; }

    /// <summary>
    ///     This property contains how often <c>CloudDnsZoneSyncService</c> pushes (for
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />) or pulls (for
    ///     <see cref="ZoneTypeEnum.CloudSecondary" />) this zone with its cloud provider. It plays
    ///     the part of a secondary's refresh interval, since a REST API has no equivalent of an RFC
    ///     1996 NOTIFY.
    /// </summary>
    public TimeSpan CloudSyncInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property controls whether CAA auto-management reconciles this zone's CAA records when
    ///     its ACME enrollment or CA selection changes. <see langword="null" /> (the default) uses
    ///     <c>CaaAutoManagementOptions.DefaultEnabled</c>, as <see cref="CaaIssuerDomain" /> does for
    ///     its setting. It only applies to <see cref="ZoneTypeEnum.Primary" /> and
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> zones, whose content this server owns.
    /// </summary>
    public bool? AutoManageCaaRecords { get; set; }

    /// <summary>
    ///     This property contains the CA issuer domain CAA auto-management writes into this zone's
    ///     CAA records (for example <c>letsencrypt.org</c>), or <see langword="null" /> to use
    ///     <c>CaaAutoManagementOptions.DefaultIssuerDomain</c>. It is an explicit setting rather
    ///     than derived from <c>AcmeOptions.DirectoryUri</c>, because guessing the domain from a URL
    ///     can go wrong, and a wrong CAA <c>issue</c> value blocks certificate issuance.
    /// </summary>
    public string CaaIssuerDomain { get; set; }

    /// <summary>
    ///     This property controls whether reverse-zone sync mirrors this zone's A/AAAA records into
    ///     the enclosing <c>in-addr.arpa</c> or <c>ip6.arpa</c> zone. <see langword="null" /> (the
    ///     default) uses <c>PtrSyncOptions.DefaultEnabled</c>, like
    ///     <see cref="AutoManageCaaRecords" />. That default is <see langword="false" />, unlike the
    ///     CAA one, because creating a reverse zone and writing records into it is a bigger surprise
    ///     than reconciling CAA records on a zone already enrolled in ACME. It only applies to
    ///     <see cref="ZoneTypeEnum.Primary" /> and <see cref="ZoneTypeEnum.CloudPrimary" /> zones.
    /// </summary>
    public bool? AutoManagePtrRecords { get; set; }

    /// <summary>
    ///     This property contains the prefix length, in bits, that reverse-zone sync uses to group
    ///     this zone's A-record addresses into IPv4 reverse zones, or <see langword="null" /> to use
    ///     <c>PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits</c>. <c>24</c>, the default, uses
    ///     ordinary octet-aligned zones. <c>25</c> to <c>30</c> use RFC 2317 classless
    ///     <c>"{start}/{prefix}"</c> zone names, and the <c>/24</c> parent zone gets the CNAME
    ///     delegation records of RFC 2317 §4; see <c>Server.Authoritative.Ptr.ReverseZoneNameCalculator</c>.
    ///     It applies to the same zone types as <see cref="AutoManagePtrRecords" />.
    /// </summary>
    public int? PtrIpv4PrefixLength { get; set; }

    /// <summary>
    ///     This property contains the prefix length, in bits, that reverse-zone sync uses to group
    ///     this zone's AAAA-record addresses into IPv6 reverse zones, or <see langword="null" /> to
    ///     use <c>PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits</c>. It must be a multiple of
    ///     <c>4</c>, because each <c>ip6.arpa</c> label is one hex nibble (RFC 3596); the REST API
    ///     checks this. It applies to the same zone types as <see cref="AutoManagePtrRecords" />.
    /// </summary>
    public int? PtrIpv6PrefixLength { get; set; }

    /// <summary>
    ///     This property controls which transport carries this zone's AXFR/IXFR traffic. For a
    ///     <see cref="ZoneTypeEnum.Secondary" />, <see cref="ZoneTypeEnum.Stub" /> or
    ///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone it is the transport
    ///     <c>SecondaryZoneRefreshService</c> uses to pull from <see cref="PrimaryServerAddresses" />.
    ///     It defaults to <see cref="ZoneTransferTransportEnum.Plain" />. TSIG
    ///     (<see cref="TsigKeyName" />) still does the authentication; RFC 9103/9250 encryption only
    ///     adds confidentiality.
    ///     <para>
    ///         It has no effect when this server is the <see cref="ZoneTypeEnum.Primary" />: an
    ///         inbound transfer is served over whatever transport it arrived on (TCP, TLS or QUIC),
    ///         with TSIG checked as usual. There is no "require encrypted transfer" setting; an
    ///         operator enforces encryption from the secondary side by not using a plain transport.
    ///     </para>
    /// </summary>
    public ZoneTransferTransportEnum TransferTransport { get; set; } =
        ZoneTransferTransportEnum.Plain;

    /// <summary>
    ///     This property contains the identifier of the <see cref="ZoneTypeEnum.Catalog" /> zone this
    ///     zone belongs to, or <see langword="null" /> if none. On the authoring node it applies to
    ///     <see cref="ZoneTypeEnum.Primary" /> and <see cref="ZoneTypeEnum.CloudPrimary" /> zones,
    ///     like <see cref="AutoManageCaaRecords" />, since only a zone whose content this server owns
    ///     can be advertised in a catalog.
    ///     <para>
    ///         It also marks a zone created by the <see cref="ZoneTypeEnum.SecondaryCatalog" />
    ///         consumer: a zone first learned from a catalog's member list is created as
    ///         <see cref="ZoneTypeEnum.Secondary" /> with this field set to the catalog zone's
    ///         identifier. A later catalog update can then tell a zone it created, which is safe to
    ///         remove when the catalog drops it, from an operator's zone with the same name, as
    ///         <see cref="Data.Abstractions.Entity.RecordEntity.ManagedByFeature" /> does for
    ///         records.
    ///     </para>
    /// </summary>
    public Guid? MemberOfCatalogZoneId { get; set; }
}

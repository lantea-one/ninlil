using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one set of credentials for a cloud DNS provider (Cloudflare, Route
///     53, DigitalOcean), used to sync a <see cref="ZoneEntity" /> of type
///     <see cref="ZoneTypeEnum.CloudPrimary" /> or <see cref="ZoneTypeEnum.CloudSecondary" />.
///     Several zones can share one set (one Cloudflare account hosting several zones, for
///     example), so it is an entity rather than columns on <see cref="ZoneEntity" />.
/// </summary>
public class CloudDnsProviderCredentialEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen name of this credential set, shown with a
    ///     zone's cloud sync settings, since a provider API key has no readable name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the provider these credentials are for, which also decides the
    ///     shape of the decrypted <see cref="CredentialsEncrypted" /> JSON (an API token for
    ///     Cloudflare and DigitalOcean; an access key ID and secret access key for Route 53).
    /// </summary>
    public CloudDnsProviderTypeEnum ProviderType { get; set; }

    /// <summary>
    ///     This property contains the secret material, encrypted at rest with <c>IDataProtector</c>
    ///     as <see cref="TsigKeyEntity.SecretEncrypted" /> is. The plaintext is a small JSON object
    ///     whose keys depend on <see cref="ProviderType" /> (for example <c>{"ApiToken":"..."}</c>
    ///     for Cloudflare or <c>{"AccessKeyId":"...","SecretAccessKey":"..."}</c> for Route 53),
    ///     rather than many mostly empty columns. <see cref="RecordEntity.DataJson" /> uses the same
    ///     approach.
    /// </summary>
    public string CredentialsEncrypted { get; set; }

    /// <summary>
    ///     This property indicates whether this credential set is disabled. A disabled set is kept
    ///     but treated as missing: zones that use it stop syncing until it is re-enabled or the zone
    ///     is pointed at another set.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this credential set.
    /// </summary>
    public string Comments { get; set; }
}

using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single set of credentials for a cloud DNS provider (Cloudflare,
///     Route 53, DigitalOcean), used to sync a <see cref="ZoneEntity" /> whose
///     <see cref="ZoneEntity.Type" /> is <see cref="ZoneTypeEnum.CloudPrimary" /> or
///     <see cref="ZoneTypeEnum.CloudSecondary" />. Multiple zones may share one set of
///     credentials (an operator's single Cloudflare account, for example, hosting several
///     zones), so this is its own entity rather than a column on <see cref="ZoneEntity" /> itself.
/// </summary>
public class CloudDnsProviderCredentialEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable name for this credential
    ///     set -- shown wherever a zone's own cloud sync configuration is displayed, since a
    ///     provider-assigned API key/token has no meaningful name of its own.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains which provider these credentials authenticate against, and so
    ///     which shape <see cref="CredentialsEncrypted" />'s own decrypted JSON takes (an API
    ///     token for Cloudflare/DigitalOcean; an access key ID and secret access key for
    ///     Route 53).
    /// </summary>
    public CloudDnsProviderTypeEnum ProviderType { get; set; }

    /// <summary>
    ///     This property contains this credential set's own secret material, encrypted at rest
    ///     via <c>IDataProtector</c> -- the same pattern <see cref="TsigKeyEntity.SecretEncrypted" />
    ///     already establishes. The decrypted plaintext is a small JSON object whose keys depend
    ///     on <see cref="ProviderType" /> (for example, <c>{"ApiToken":"..."}</c> for Cloudflare,
    ///     or <c>{"AccessKeyId":"...","SecretAccessKey":"..."}</c> for Route 53), rather than a
    ///     dozen mostly-unused columns -- the same "opaque payload, shape owned by the consumer"
    ///     choice <see cref="RecordEntity.DataJson" /> already makes.
    /// </summary>
    public string CredentialsEncrypted { get; set; }

    /// <summary>
    ///     This property indicates whether this credential set is currently disabled. A disabled
    ///     credential set is kept on record but rejected as though it did not exist -- any zone
    ///     referencing it stops syncing until either it is re-enabled or the zone is repointed at
    ///     a different one.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this credential set.
    /// </summary>
    public string Comments { get; set; }
}

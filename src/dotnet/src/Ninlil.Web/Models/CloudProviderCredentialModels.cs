using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new cloud DNS provider credential set
///     (<c>POST /api/cloud-provider-credentials</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for this credential set.</param>
/// <param name="ProviderType">
///     Which cloud DNS provider these credentials authenticate against, and so which keys
///     <see cref="Credentials" /> must supply -- <c>ApiToken</c> for Cloudflare/DigitalOcean, or
///     <c>AccessKeyId</c>/<c>SecretAccessKey</c> for Route 53.
/// </param>
/// <param name="Credentials">
///     The credential set's own secret material, keyed by field name -- shape depends on
///     <see cref="ProviderType" />. Encrypted at rest immediately; the exact values supplied here
///     are returned exactly once, in this endpoint's own response, and never again afterward.
/// </param>
/// <param name="IsDisabled">Whether the credential set starts out disabled.</param>
/// <param name="Comments">Free-form operator notes about the credential set.</param>
public record CloudProviderCredentialCreateRequest(
    string Name,
    CloudDnsProviderTypeEnum ProviderType,
    Dictionary<string, string> Credentials,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries the fields used to fully replace an existing cloud DNS provider
///     credential set (<c>PUT /api/cloud-provider-credentials/{id}</c>). Unlike a TSIG key's own
///     shared secret (see <see cref="TsigKeyWriteRequest" />), a cloud provider credential's own
///     secret material genuinely is something an operator supplied and may need to rotate --
///     Cloudflare/Route 53/DigitalOcean all expect a real operator to periodically rotate an API
///     token or access key themselves, entirely outside this project's own control. A <c>PUT</c>
///     therefore replaces <see cref="Credentials" /> too, alongside every other field, matching
///     ordinary full-replacement semantics -- there is no reveal-again concern here the way there
///     would be for a server-generated secret, since the caller already knows the plaintext value
///     they are supplying in this very request. To change only metadata (name, comments,
///     disabled) without touching the stored credentials, use <c>PATCH</c> instead and omit
///     <c>credentials</c> from the patch document.
/// </summary>
/// <param name="Name">The credential set's name.</param>
/// <param name="ProviderType">Which cloud DNS provider these credentials authenticate against.</param>
/// <param name="Credentials">The credential set's own secret material, replacing it entirely.</param>
/// <param name="IsDisabled">Whether the credential set is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about the credential set.</param>
public record CloudProviderCredentialWriteRequest(
    string Name,
    CloudDnsProviderTypeEnum ProviderType,
    Dictionary<string, string> Credentials,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a cloud DNS provider credential set's own metadata, for list,
///     single-item, and (nested inside <see cref="CloudProviderCredentialCreateResponse" />)
///     creation responses. The credential set's own secret material is never included here -- see
///     <see cref="CloudProviderCredentialCreateResponse" /> for the one place it ever appears.
/// </summary>
public record CloudProviderCredentialDetail(
    Guid Id,
    string Name,
    CloudDnsProviderTypeEnum ProviderType,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries a newly created cloud DNS provider credential set's own metadata
///     alongside its plaintext secret material -- the only response that will ever include
///     <see cref="Secrets" />, per this project's reveal-once-then-omitted exposure policy for
///     every secret-bearing entity type.
/// </summary>
/// <param name="Credential">The newly created credential set's own metadata.</param>
/// <param name="Secrets">The credential set's own secret material, keyed by field name.</param>
public record CloudProviderCredentialCreateResponse(
    CloudProviderCredentialDetail Credential,
    IReadOnlyDictionary<string, string> Secrets
);

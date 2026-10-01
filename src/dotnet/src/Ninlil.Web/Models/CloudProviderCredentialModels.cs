using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new cloud DNS provider credential set
///     (<c>POST /api/cloud-provider-credentials</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for the credential set.</param>
/// <param name="ProviderType">
///     Which cloud DNS provider the credentials authenticate against, and so which keys
///     <see cref="Credentials" /> must supply: <c>ApiToken</c> for Cloudflare and DigitalOcean,
///     or <c>AccessKeyId</c> and <c>SecretAccessKey</c> for Route 53.
/// </param>
/// <param name="Credentials">
///     The secret material, keyed by field name; the keys depend on <see cref="ProviderType" />.
///     It is stored encrypted, and the values are returned once, in this endpoint's response.
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
///     credential set (<c>PUT /api/cloud-provider-credentials/{id}</c>). Unlike the shared secret
///     of a TSIG key (see <see cref="TsigKeyWriteRequest" />), these secrets come from the
///     operator, who rotates them at the provider, so a <c>PUT</c> replaces
///     <see cref="Credentials" /> along with every other field. Nothing new is revealed, since the
///     caller supplies the values. To change only the name, comments or disabled flag, use
///     <c>PATCH</c> and leave out <c>credentials</c>.
/// </summary>
/// <param name="Name">The credential set's name.</param>
/// <param name="ProviderType">Which cloud DNS provider these credentials authenticate against.</param>
/// <param name="Credentials">The secret material, replacing what is stored.</param>
/// <param name="IsDisabled">Whether the credential set is disabled.</param>
/// <param name="Comments">Free-form operator notes about the credential set.</param>
public record CloudProviderCredentialWriteRequest(
    string Name,
    CloudDnsProviderTypeEnum ProviderType,
    Dictionary<string, string> Credentials,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a cloud DNS provider credential set without its secrets, as returned by
///     the list and get endpoints and nested in
///     <see cref="CloudProviderCredentialCreateResponse" />, the one response that includes the
///     secrets.
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
///     This class carries a newly created cloud DNS provider credential set with its plaintext
///     secrets. It is the only response that includes <see cref="Secrets" />: like every
///     secret-bearing entity, a secret is revealed once at creation and omitted afterwards.
/// </summary>
/// <param name="Credential">The new credential set, without secrets.</param>
/// <param name="Secrets">The secret material as submitted, keyed by field name.</param>
public record CloudProviderCredentialCreateResponse(
    CloudProviderCredentialDetail Credential,
    IReadOnlyDictionary<string, string> Secrets
);

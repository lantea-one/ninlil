using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.Sso;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="OidcSsoOptions" /> -- the REST
///     management surface for this server's own generic OpenID Connect admin-login federation.
///     Every action requires the <c>ssosettings:read</c> or <c>ssosettings:write</c> permission.
///     <para>
///         <b>Every field is written to the configuration file immediately, but
///         <c>enable</c>/<c>authority</c>/<c>clientId</c> still need a process restart to actually
///         change whether federated sign-in is available</b> -- see
///         <see cref="RuntimeSettingsWriteResult.SsoRestartRequiredNote" />'s own doc comment for
///         the full structural reason (the OpenID Connect scheme is registered exactly once,
///         before this container even exists to serve a live options read).
///     </para>
///     <para>
///         <see cref="OidcSsoOptions.ClientSecretEncrypted" /> is handled the same "accept
///         plaintext in, encrypt before it's written to disk, never return it on a read" way this
///         controller's own <see cref="Patch" /> action documents in detail -- see
///         <see cref="OidcClientSecretProtector" />'s own doc comment for why this is the first
///         configuration-file secret field this project encrypts at rest, unlike
///         <c>EmailSenderOptions.Password</c>'s existing plaintext precedent.
///     </para>
/// </summary>
/// <param name="ssoOptions">This host's own currently-effective SSO settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
/// <param name="dataProtectionProvider">The provider the client secret is encrypted through before it is written.</param>
[ApiController]
[Route("api/sso-settings")]
public class OidcSsoSettingsController(
    IOptionsMonitor<OidcSsoOptions> ssoOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live) OIDC SSO settings.
    ///     <see cref="OidcSsoOptions.ClientSecretEncrypted" /> is never returned -- see
    ///     <see cref="OidcSsoSettingsDetail.ClientSecretConfigured" />'s own doc comment.
    /// </summary>
    [HttpGet]
    [RequirePermission("ssosettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(OidcSsoSettingsDetailExample))]
    public ActionResult<OidcSsoSettingsDetail> Get() => Ok(ToDetail(ssoOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>Identity:Sso</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file -- including <c>clientSecret</c>, letting an operator change, say,
    ///     only <c>displayName</c> without ever having to resupply a credential this endpoint's own
    ///     <see cref="Get" /> action never reveals back to them.
    /// </summary>
    [HttpPatch]
    [RequirePermission("ssosettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(OidcSsoSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("displayName", out JsonElement displayNameElement))
        {
            string displayName = displayNameElement.GetString();

            if (string.IsNullOrWhiteSpace(displayName))
                throw new ValidationException("'displayName' must not be blank.");

            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.DisplayName),
                    displayName
                )
            );
        }

        if (patch.TryGetMergePatchField("authority", out JsonElement authorityElement))
        {
            string authority = authorityElement.GetString();

            if (
                string.IsNullOrWhiteSpace(authority)
                || !Uri.TryCreate(authority, UriKind.Absolute, out Uri parsedAuthority)
                || (
                    parsedAuthority.Scheme != Uri.UriSchemeHttp
                    && parsedAuthority.Scheme != Uri.UriSchemeHttps
                )
            )
                throw new ValidationException(
                    "'authority' must be an absolute http:// or https:// URL -- your identity "
                        + "provider's own issuer URL."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.Authority),
                    authority
                )
            );
        }

        if (patch.TryGetMergePatchField("clientId", out JsonElement clientIdElement))
        {
            string clientId = clientIdElement.GetString();

            if (string.IsNullOrWhiteSpace(clientId))
                throw new ValidationException("'clientId' must not be blank.");

            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.ClientId),
                    clientId
                )
            );
        }

        // A blank string is accepted here (unlike 'clientId') -- it clears a previously-configured
        // secret, the same "blank clears, absent leaves untouched" distinction
        // SmtpSettingsController's own 'password' field already establishes.
        if (patch.TryGetMergePatchField("clientSecret", out JsonElement clientSecretElement))
        {
            string clientSecret = clientSecretElement.GetString() ?? string.Empty;
            string clientSecretEncrypted = string.IsNullOrEmpty(clientSecret)
                ? string.Empty
                : new OidcClientSecretProtector(dataProtectionProvider).Protect(clientSecret);

            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.ClientSecretEncrypted),
                    clientSecretEncrypted
                )
            );
        }

        if (patch.TryGetMergePatchField("scopes", out JsonElement scopesElement))
            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.Scopes),
                    ParseScopes(scopesElement)
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(
            new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.SsoRestartRequiredNote)
        );
    }

    /// <summary>
    ///     This method parses a <c>scopes</c> merge-patch field (a JSON array of scope names) into
    ///     the validated list <see cref="OidcSsoOptions.Scopes" /> binds from, rejecting anything
    ///     that isn't a non-blank string array -- and, specifically, an array that omits the
    ///     mandatory <c>openid</c> scope, since a request that never asks for it is not an OpenID
    ///     Connect request at all, merely bare OAuth 2.0.
    /// </summary>
    private static List<string> ParseScopes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException(
                "'scopes' must be a JSON array of OpenID Connect scope names, for example "
                    + "[\"openid\", \"profile\", \"email\"]."
            );

        List<string> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
                throw new ValidationException(
                    "Every entry in 'scopes' must be a non-blank string."
                );

            parsed.Add(item.GetString());
        }

        if (!parsed.Contains("openid", StringComparer.Ordinal))
            throw new ValidationException(
                "'scopes' must include \"openid\" -- without it, this is not an OpenID Connect "
                    + "request at all."
            );

        return parsed;
    }

    /// <summary>
    ///     This method builds <see cref="OidcSsoSettingsDetail" /> from a live
    ///     <see cref="OidcSsoOptions" /> snapshot.
    /// </summary>
    private static OidcSsoSettingsDetail ToDetail(OidcSsoOptions options) =>
        new(
            options.Enable,
            options.DisplayName,
            options.Authority,
            options.ClientId,
            !string.IsNullOrEmpty(options.ClientSecretEncrypted),
            options.Scopes
        );
}

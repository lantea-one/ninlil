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
///     This controller reads and writes <see cref="OidcSsoOptions" />, the settings for signing in
///     to the admin console through a generic OpenID Connect identity provider. Reading needs
///     <c>ssosettings:read</c> and writing needs <c>ssosettings:write</c>.
///     <para>
///         <b>Writes reach the configuration file at once, but only <c>displayName</c> takes
///         effect without a restart.</b> The OpenID Connect scheme is registered once at startup
///         from the settings as they were then, so <c>enable</c>, <c>authority</c>,
///         <c>clientId</c>, <c>clientSecret</c> and <c>scopes</c> apply after a restart (see
///         <see cref="RuntimeSettingsWriteResult.SsoRestartRequiredNote" />). A write of only
///         <c>displayName</c> returns <see cref="RuntimeSettingsWriteResult.HotReloadedNote" />.
///     </para>
///     <para>
///         The client secret is accepted in plain text, encrypted with
///         <see cref="OidcClientSecretProtector" /> before it is written to disk, and never
///         returned. The SMTP password is protected the same way, by
///         <c>SmtpPasswordProtector</c>. The plaintext variable <c>NINLIL_IDENTITY_SSO_CLIENT_SECRET</c>
///         (<see cref="OidcSsoOptions.ClientSecret" />) overrides the stored secret; <see cref="Get" />
///         reports that, and whether the stored secret can no longer be decrypted.
///     </para>
/// </summary>
/// <param name="ssoOptions">The current SSO settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
/// <param name="dataProtectionProvider">Encrypts the client secret before it is written.</param>
[ApiController]
[Route("api/sso-settings")]
public class OidcSsoSettingsController(
    IOptionsMonitor<OidcSsoOptions> ssoOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current SSO settings. The client secret is never returned, only
    ///     whether one is set (see <see cref="OidcSsoSettingsDetail.ClientSecretConfigured" />),
    ///     whether the plaintext variable overrides it and whether the stored one is unreadable.
    ///     Requires <c>ssosettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("ssosettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(OidcSsoSettingsDetailExample))]
    public ActionResult<OidcSsoSettingsDetail> Get() => Ok(ToDetail(ssoOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>Identity:Sso</c> as an RFC 7396 merge patch,
    ///     through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value, including <c>clientSecret</c>, so an operator
    ///     can change only <c>displayName</c> without resupplying a secret <see cref="Get" /> never
    ///     shows. A blank <c>clientSecret</c> clears it. Returns 400 for a blank display name or
    ///     client id, an authority that is not an absolute http(s) URL, or scopes without
    ///     <c>openid</c>. Requires <c>ssosettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("ssosettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(OidcSsoSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.Enable),
                    enable
                )
            );

        if (patch.TryGetMergePatchString("displayName", out string displayName, allowNull: false))
        {
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

        if (patch.TryGetMergePatchString("authority", out string authority, allowNull: false))
        {
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

        if (patch.TryGetMergePatchString("clientId", out string clientId, allowNull: false))
        {
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

        // A blank string clears the stored secret (unlike 'clientId', which must not be blank); an
        // absent field leaves it alone, as with the SMTP 'password'.
        if (
            patch.TryGetMergePatchString(
                "clientSecret",
                out string clientSecretText,
                allowNull: true
            )
        )
        {
            string clientSecret = clientSecretText ?? string.Empty;
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

            // A plaintext value hand-edited into the file must not outlive this write. This
            // blanks only the file's key; the environment variable is outside the file and keeps
            // overriding (Get reports it).
            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.ClientSecret),
                    string.Empty
                )
            );
        }

        if (patch.TryGetMergePatchStringList("scopes", out List<string> scopes, allowNull: false))
            settings.Add(
                new PresetConfigurationSetting(
                    OidcSsoOptions.SectionName,
                    nameof(OidcSsoOptions.Scopes),
                    ValidateScopes(scopes)
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        // displayName is read per request (SsoController), so a write of only that field needs no
        // restart; every other field is captured when the authentication scheme is registered.
        bool onlyDisplayName = settings.All(setting =>
            setting.Key == nameof(OidcSsoOptions.DisplayName)
        );

        return Ok(
            new RuntimeSettingsWriteResult(
                onlyDisplayName
                    ? RuntimeSettingsWriteResult.HotReloadedNote
                    : RuntimeSettingsWriteResult.SsoRestartRequiredNote
            )
        );
    }

    /// <summary>
    ///     This method checks the <c>scopes</c> field, a JSON array of scope names, and rejects
    ///     blank entries. The list must include <c>openid</c>; without it the request is plain
    ///     OAuth 2.0, not OpenID Connect.
    /// </summary>
    private static List<string> ValidateScopes(List<string> scopes)
    {
        if (scopes.Any(string.IsNullOrWhiteSpace))
            throw new ValidationException("Every entry in 'scopes' must be a non-blank string.");

        if (!scopes.Contains("openid", StringComparer.Ordinal))
            throw new ValidationException(
                "'scopes' must include \"openid\" -- without it, this is not an OpenID Connect "
                    + "request at all."
            );

        return scopes;
    }

    /// <summary>
    ///     This method builds <see cref="OidcSsoSettingsDetail" /> from the current
    ///     <see cref="OidcSsoOptions" />. A plaintext secret overrides the stored one, so the stored
    ///     secret is only checked for readability when no plaintext secret is set.
    /// </summary>
    private OidcSsoSettingsDetail ToDetail(OidcSsoOptions options)
    {
        bool fromEnvironment = !string.IsNullOrEmpty(options.ClientSecret);
        bool unreadable =
            !fromEnvironment
            && !new OidcClientSecretProtector(dataProtectionProvider).TryUnprotect(
                options.ClientSecretEncrypted,
                out _
            );

        return new OidcSsoSettingsDetail(
            options.Enable,
            options.DisplayName,
            options.Authority,
            options.ClientId,
            fromEnvironment || !string.IsNullOrEmpty(options.ClientSecretEncrypted),
            options.Scopes,
            fromEnvironment,
            unreadable
        );
    }
}

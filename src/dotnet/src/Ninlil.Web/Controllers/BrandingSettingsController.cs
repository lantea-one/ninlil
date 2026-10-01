using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer.Model;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Branding;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes branding: the admin console's site title and logo
///     (<see cref="BrandingOptions" />) and the <see cref="AuthoritativeServerOptions" /> fields
///     that answer CHAOS-class <c>version.bind</c> and <c>hostname.bind</c> TXT queries (see
///     <c>AuthoritativeQueryResolver.TryResolveChaosQuery</c>). Keeping them together means the
///     "server name" an operator sets is the one DNS clients see, not a cosmetic copy.
///     <para>
///         <see cref="Get" /> and <see cref="Patch" /> need <c>branding:read</c> and
///         <c>branding:write</c>. <see cref="GetEffective" /> needs only a signed-in caller, since
///         it returns just the title and logo (see <see cref="BrandingEffectiveDetail" />).
///     </para>
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> Both
///         <see cref="BrandingOptions" /> and the CHAOS settings are read through
///         <see cref="IOptionsMonitor{TOptions}" />; the singleton
///         <see cref="Ninlil.Dns.Server.Authoritative.Resolution.AuthoritativeQueryResolver" />
///         reads <c>CurrentValue</c> on every CHAOS query.
///     </para>
/// </summary>
/// <param name="brandingOptions">The current site title and logo.</param>
/// <param name="authoritativeOptions">The current CHAOS query settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/branding-settings")]
public class BrandingSettingsController(
    IOptionsMonitor<BrandingOptions> brandingOptions,
    IOptionsMonitor<AuthoritativeServerOptions> authoritativeOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the branding and CHAOS query settings, including any change
    ///     <see cref="Patch" /> has made. Requires <c>branding:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("branding:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(BrandingSettingsDetailExample))]
    public ActionResult<BrandingSettingsDetail> Get() =>
        Ok(
            new BrandingSettingsDetail(
                brandingOptions.CurrentValue.SiteTitle,
                brandingOptions.CurrentValue.LogoDataUri,
                authoritativeOptions.CurrentValue.HostnameString,
                authoritativeOptions.CurrentValue.VersionString,
                authoritativeOptions.CurrentValue.EnableChaosVersionQueries
            )
        );

    /// <summary>
    ///     This action returns only the site title and logo, which every signed-in session needs to
    ///     render the admin console. Requires only a signed-in caller (see
    ///     <see cref="BrandingEffectiveDetail" />).
    /// </summary>
    [HttpGet("effective")]
    [Authorize]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(BrandingEffectiveDetailExample))]
    public ActionResult<BrandingEffectiveDetail> GetEffective() =>
        Ok(
            new BrandingEffectiveDetail(
                brandingOptions.CurrentValue.SiteTitle,
                brandingOptions.CurrentValue.LogoDataUri
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Branding</c> (<c>siteTitle</c>,
    ///     <c>logoDataUri</c>) and <c>DnsServer:Authoritative</c> (<c>hostnameString</c>,
    ///     <c>versionString</c>, <c>enableChaosVersionQueries</c>) as an RFC 7396 merge patch,
    ///     through <see cref="PresetConfigurationFileApplier" />. One write may touch either
    ///     section or both. An absent field keeps its value; a JSON <c>null</c> clears
    ///     <c>siteTitle</c> or <c>logoDataUri</c>. Every field takes effect at once. Returns 400
    ///     for a blank hostname or version string or an invalid logo. Requires
    ///     <c>branding:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("branding:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(BrandingSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("siteTitle", out JsonElement siteTitleElement))
            settings.Add(
                new PresetConfigurationSetting(
                    BrandingOptions.SectionName,
                    nameof(BrandingOptions.SiteTitle),
                    siteTitleElement.ValueKind == JsonValueKind.Null
                        ? null
                        : siteTitleElement.GetString()
                )
            );

        if (patch.TryGetMergePatchField("logoDataUri", out JsonElement logoDataUriElement))
            settings.Add(
                new PresetConfigurationSetting(
                    BrandingOptions.SectionName,
                    nameof(BrandingOptions.LogoDataUri),
                    logoDataUriElement.ValueKind == JsonValueKind.Null
                        ? null
                        : ValidateLogoDataUri(logoDataUriElement.GetString())
                )
            );

        if (patch.TryGetMergePatchField("hostnameString", out JsonElement hostnameStringElement))
        {
            string hostnameString = hostnameStringElement.GetString();

            if (string.IsNullOrWhiteSpace(hostnameString))
                throw new ValidationException("'hostnameString' may not be blank.");

            settings.Add(
                new PresetConfigurationSetting(
                    AuthoritativeServerOptions.SectionName,
                    nameof(AuthoritativeServerOptions.HostnameString),
                    hostnameString
                )
            );
        }

        if (patch.TryGetMergePatchField("versionString", out JsonElement versionStringElement))
        {
            string versionString = versionStringElement.GetString();

            if (string.IsNullOrWhiteSpace(versionString))
                throw new ValidationException("'versionString' may not be blank.");

            settings.Add(
                new PresetConfigurationSetting(
                    AuthoritativeServerOptions.SectionName,
                    nameof(AuthoritativeServerOptions.VersionString),
                    versionString
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "enableChaosVersionQueries",
                out JsonElement enableChaosElement
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    AuthoritativeServerOptions.SectionName,
                    nameof(AuthoritativeServerOptions.EnableChaosVersionQueries),
                    enableChaosElement.GetBoolean()
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.HotReloadedNote));
    }

    /// <summary>
    ///     This method checks a supplied logo with <see cref="DataUriModel" />: it must be a
    ///     well-formed data URI, its MIME type must start with <c>image/</c>, and its decoded size
    ///     must not exceed <see cref="BrandingOptions.MaxLogoSizeBytes" />. A blank value clears
    ///     the logo.
    /// </summary>
    private static string ValidateLogoDataUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DataUriModel.TryParse(value, out DataUriModel dataUri) || !dataUri.IsValid())
            throw new ValidationException(
                "'logoDataUri' is not a well-formed data URI (expected \"data:<mime-type>;base64,<data>\")."
            );

        if (dataUri.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            throw new ValidationException(
                $"'logoDataUri' must be an image (its declared MIME type was "
                    + $"'{dataUri.MimeType ?? "unknown"}')."
            );

        if (dataUri.Binary.Length > BrandingOptions.MaxLogoSizeBytes)
            throw new ValidationException(
                $"'logoDataUri' is too large -- the decoded image may not exceed "
                    + $"{BrandingOptions.MaxLogoSizeBytes / 1024} KB."
            );

        return value;
    }
}

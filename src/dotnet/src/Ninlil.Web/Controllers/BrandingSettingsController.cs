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
///     This controller reads and writes this host's own global branding: <see cref="BrandingOptions" />'s
///     own site title/logo, plus the already-existing <see cref="AuthoritativeServerOptions" />
///     fields that drive real CHAOS-class <c>version.bind</c>/<c>hostname.bind</c> TXT query
///     answers (see <c>AuthoritativeQueryResolver.TryResolveChaosQuery</c>, a private method on
///     that resolver, hence plain text here rather than a cref)
///     -- exposed together on one settings surface rather than building a second, cosmetic-only
///     "custom hostname" string that does not actually reach the DNS protocol layer.
///     <para>
///         <b>The two admin actions (<see cref="Get" />/<see cref="Patch" />) require the
///         <c>branding:read</c>/<c>branding:write</c> permission</b>, this project's usual
///         <c>resource:read</c>/<c>resource:write</c> shape for every other settings controller.
///         <b>The self-service action (<see cref="GetEffective" />) is gated by
///         <see cref="AuthorizeAttribute" /> alone</b>, deliberately: see
///         <see cref="BrandingEffectiveDetail" />'s own doc comment for why the site title/logo
///         specifically carry no confidentiality concern the CHAOS-query fields do not share.
///     </para>
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <see cref="BrandingOptions" />
///         is bound via <see cref="IOptionsMonitor{TOptions}" /> from the start (see its own doc
///         comment). <see cref="AuthoritativeServerOptions" />'s own real consumer,
///         <see cref="Ninlil.Dns.Server.Authoritative.Resolution.AuthoritativeQueryResolver" />, used to read
///         it via the snapshotted <see cref="IOptions{TOptions}" />, cached for the life of the
///         process -- a write reaching the configuration file would never have been observed by an
///         already-running instance at all, the same functional gap already found and fixed
///         elsewhere in this project for other options classes. Converted here the same way: that
///         singleton-registered resolver now reads <see cref="IOptionsMonitor{TOptions}" />'s own
///         <c>CurrentValue</c> fresh on every single CHAOS-class query it answers.
///     </para>
/// </summary>
/// <param name="brandingOptions">This host's own currently-effective branding, live.</param>
/// <param name="authoritativeOptions">This host's own currently-effective CHAOS-query settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/branding-settings")]
public class BrandingSettingsController(
    IOptionsMonitor<BrandingOptions> brandingOptions,
    IOptionsMonitor<AuthoritativeServerOptions> authoritativeOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live -- reflecting any write
    ///     <see cref="Patch" /> has made, even without a restart) branding configuration.
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
    ///     This action reads only the small, non-sensitive slice of this host's own branding every
    ///     authenticated session needs to render the SPA's own page title/logo correctly -- see
    ///     <see cref="BrandingEffectiveDetail" />'s own doc comment for why this needs no specific
    ///     permission, unlike <see cref="Get" />.
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
    ///     This action merges a partial update into <c>DnsServer:Branding</c> (<c>siteTitle</c>/
    ///     <c>logoDataUri</c>) and/or <c>DnsServer:Authoritative</c> (<c>hostnameString</c>/
    ///     <c>versionString</c>/<c>enableChaosVersionQueries</c>) via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style -- a single write can touch either section, or both, at once. A
    ///     field absent from <paramref name="patch" /> is left exactly as it was in the file; a
    ///     field present with a JSON <c>null</c> clears <c>siteTitle</c>/<c>logoDataUri</c> back to
    ///     "unset" (this project's every other settings controller treats a JSON <c>null</c> the
    ///     same way for a nullable field). Every field takes effect immediately -- see this class's
    ///     own doc comment.
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
    ///     This method validates a supplied logo, reusing <see cref="DataUriModel" />'s own
    ///     parsing (this project's confirmed "logo as a data URI" storage mechanism) rather than
    ///     accepting an arbitrary string: it must actually be a well-formed data URI, its declared
    ///     MIME type must start with <c>image/</c> (never an arbitrary binary payload masquerading
    ///     as a logo), and its decoded size may not exceed
    ///     <see cref="BrandingOptions.MaxLogoSizeBytes" />.
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

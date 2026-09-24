using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Appearance;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own global default appearance
///     (<see cref="AppearanceOptions" />, the palette and light/dark mode the Angular admin SPA's
///     own theming engine renders for every signed-in user who has not set an override of their
///     own), and lets any signed-in caller manage their own personal override on top of it.
///     <para>
///         <b>The two admin actions (<see cref="Get" />/<see cref="Patch" />) require the
///         <c>appearance:read</c>/<c>appearance:write</c> permission</b>, this project's usual
///         <c>resource:read</c>/<c>resource:write</c> shape for every other settings controller.
///         <b>The two self-service actions (<see cref="GetMine" />/<see cref="PatchMine" />) are
///         gated by <see cref="AuthorizeAttribute" /> alone</b>, deliberately: any authenticated
///         account may freely choose its own appearance, exactly the same "self-service, no
///         specific permission needed" bar <see cref="TwoFactorController" />'s own account-
///         security actions already set.
///     </para>
///     <para>
///         <b><see cref="Patch" />'s own write takes effect immediately, with no restart.</b>
///         <see cref="AppearanceOptions" /> is this codebase's first runtime settings class read
///         via <see cref="IOptionsMonitor{TOptions}" /> instead of the usual, restart-requiring
///         <see cref="IOptions{TOptions}" /> -- see that class's own doc comment for why this is
///         safe specifically for appearance. The write mechanism itself is unchanged: the exact
///         same <see cref="PresetConfigurationFileApplier" /> atomic merge every other runtime
///         settings controller (<c>EventBrokerSettingsController</c>,
///         <c>RateLimitingSettingsController</c>, <c>DnsCookieSettingsController</c>,
///         <c>ReverseProxySettingsController</c>) already reuses.
///     </para>
/// </summary>
/// <param name="appearanceOptions">This host's own currently-effective global default appearance, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a global-default write is merged into.</param>
/// <param name="userManager">Reads and updates the current caller's own appearance-preference override.</param>
[ApiController]
[Route("api/appearance-settings")]
public class AppearanceSettingsController(
    IOptionsMonitor<AppearanceOptions> appearanceOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    UserManager<UserEntity> userManager
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live -- reflecting any write
    ///     <see cref="Patch" /> has made, even without a restart) global default appearance.
    /// </summary>
    [HttpGet]
    [RequirePermission("appearance:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AppearanceSettingsDetailExample))]
    public ActionResult<AppearanceSettingsDetail> Get() =>
        Ok(
            new AppearanceSettingsDetail(
                appearanceOptions.CurrentValue.Palette,
                appearanceOptions.CurrentValue.Mode
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Appearance</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. Unlike every other runtime settings <c>PATCH</c> action in this
    ///     project, this one's own write is visible immediately -- see this class's own doc comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("appearance:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(AppearanceSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AppearanceSettingsPatchResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("palette", out JsonElement paletteElement))
        {
            if (
                !Enum.TryParse(
                    paletteElement.GetString(),
                    ignoreCase: true,
                    out UiThemePaletteEnum palette
                )
            )
                throw new ValidationException(
                    $"'{paletteElement.GetString()}' is not a valid palette. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<UiThemePaletteEnum>())}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    AppearanceOptions.SectionName,
                    nameof(AppearanceOptions.Palette),
                    palette.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("mode", out JsonElement modeElement))
        {
            if (!Enum.TryParse(modeElement.GetString(), ignoreCase: true, out UiThemeModeEnum mode))
                throw new ValidationException(
                    $"'{modeElement.GetString()}' is not a valid mode. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<UiThemeModeEnum>())}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    AppearanceOptions.SectionName,
                    nameof(AppearanceOptions.Mode),
                    mode.ToString()
                )
            );
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.HotReloadedNote));
    }

    /// <summary>
    ///     This action reports the current caller's own appearance preference: their own override
    ///     (if any) alongside the effective value the Angular admin SPA's own theming engine
    ///     should actually render, falling back to this host's own current global default for
    ///     whichever field (if either) the caller has not overridden.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UserAppearancePreferenceDetailExample))]
    public async Task<ActionResult<UserAppearancePreferenceDetail>> GetMine()
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        return Ok(ToDetail(user));
    }

    /// <summary>
    ///     This action merges a partial update into the current caller's own
    ///     <see cref="UserEntity.PreferredPalette" />/<see cref="UserEntity.PreferredMode" />, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was; a field present with a JSON <c>null</c> clears that field's own override,
    ///     reverting the caller back to this host's own global default for it; a field present with
    ///     a palette/mode name sets that override. This write lands directly on the caller's own
    ///     account row, so -- unlike <see cref="Patch" /> -- it needs no configuration-file
    ///     round trip and is visible on this same controller's very next read regardless.
    /// </summary>
    [HttpPatch("me")]
    [Authorize]
    [SwaggerRequestExample(
        typeof(JsonElement),
        typeof(UserAppearancePreferencePatchRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UserAppearancePreferenceDetailExample))]
    public async Task<ActionResult<UserAppearancePreferenceDetail>> PatchMine(
        [FromBody] JsonElement patch
    )
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (patch.TryGetMergePatchField("palette", out JsonElement paletteElement))
        {
            if (paletteElement.ValueKind == JsonValueKind.Null)
            {
                user.PreferredPalette = null;
            }
            else if (
                !Enum.TryParse(
                    paletteElement.GetString(),
                    ignoreCase: true,
                    out UiThemePaletteEnum palette
                )
            )
                throw new ValidationException(
                    $"'{paletteElement.GetString()}' is not a valid palette. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<UiThemePaletteEnum>())}."
                );
            else
                user.PreferredPalette = palette;
        }

        if (patch.TryGetMergePatchField("mode", out JsonElement modeElement))
        {
            if (modeElement.ValueKind == JsonValueKind.Null)
            {
                user.PreferredMode = null;
            }
            else if (
                !Enum.TryParse(modeElement.GetString(), ignoreCase: true, out UiThemeModeEnum mode)
            )
                throw new ValidationException(
                    $"'{modeElement.GetString()}' is not a valid mode. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<UiThemeModeEnum>())}."
                );
            else
                user.PreferredMode = mode;
        }

        IdentityResult result = await userManager.UpdateAsync(user);
        result.ThrowIfFailed();

        return Ok(ToDetail(user));
    }

    /// <summary>
    ///     This method builds <see cref="UserAppearancePreferenceDetail" /> for <paramref name="user" />,
    ///     resolving each effective field against this host's own current global default.
    /// </summary>
    private UserAppearancePreferenceDetail ToDetail(UserEntity user) =>
        new(
            user.PreferredPalette,
            user.PreferredMode,
            user.PreferredPalette ?? appearanceOptions.CurrentValue.Palette,
            user.PreferredMode ?? appearanceOptions.CurrentValue.Mode
        );

    /// <summary>
    ///     This method resolves the currently signed-in caller's own <see cref="UserEntity" />,
    ///     the same pattern <see cref="TwoFactorController.GetCurrentUserAsync" /> already
    ///     establishes for a self-service action.
    /// </summary>
    private async Task<UserEntity> GetCurrentUserAsync()
    {
        Guid? userId = User.GetUserId();

        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }
}

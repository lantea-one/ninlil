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
///     This controller reads and writes the default appearance of the admin console
///     (<see cref="AppearanceOptions" />: the palette and the light/dark mode for every user who
///     has not chosen their own), and lets each signed-in user manage a personal override.
///     <para>
///         <see cref="Get" /> and <see cref="Patch" /> manage the server-wide default and need
///         <c>appearance:read</c> and <c>appearance:write</c>. <see cref="GetMine" /> and
///         <see cref="PatchMine" /> manage the caller's own choice and need only a signed-in
///         caller, like the account-security actions in <see cref="TwoFactorController" />.
///     </para>
///     <para>
///         <b>A <see cref="Patch" /> takes effect at once, with no restart.</b>
///         <see cref="AppearanceOptions" /> is read through
///         <see cref="IOptionsMonitor{TOptions}" /> (see that class for why this is safe), and the
///         write goes through the same atomic <see cref="PresetConfigurationFileApplier" /> merge
///         the other settings controllers use.
///     </para>
/// </summary>
/// <param name="appearanceOptions">The current server-wide default appearance.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a default-appearance write is merged into.</param>
/// <param name="userManager">Reads and updates the caller's appearance override.</param>
[ApiController]
[Route("api/appearance-settings")]
public class AppearanceSettingsController(
    IOptionsMonitor<AppearanceOptions> appearanceOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    UserManager<UserEntity> userManager
) : ControllerBase
{
    /// <summary>
    ///     This action returns the server-wide default appearance, including any change
    ///     <see cref="Patch" /> has made. Requires <c>appearance:read</c>.
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
    ///     This action merges a partial update into <c>DnsServer:Appearance</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. The change takes effect at once. Returns 400
    ///     for an unknown palette or mode name. Requires <c>appearance:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("appearance:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(AppearanceSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AppearanceSettingsPatchResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchEnum("palette", out UiThemePaletteEnum palette))
            settings.Add(
                new PresetConfigurationSetting(
                    AppearanceOptions.SectionName,
                    nameof(AppearanceOptions.Palette),
                    palette.ToString()
                )
            );

        if (patch.TryGetMergePatchEnum("mode", out UiThemeModeEnum mode))
            settings.Add(
                new PresetConfigurationSetting(
                    AppearanceOptions.SectionName,
                    nameof(AppearanceOptions.Mode),
                    mode.ToString()
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
    ///     This action returns the caller's appearance: their override for each field, if any, and
    ///     the effective value the admin console should render, which falls back to the server-wide
    ///     default for a field without an override. Requires only a signed-in caller.
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
    ///     This action merges a partial update into the caller's
    ///     <see cref="UserEntity.PreferredPalette" /> and <see cref="UserEntity.PreferredMode" />
    ///     as an RFC 7396 merge patch: an absent field is unchanged, a JSON <c>null</c> removes the
    ///     override (falling back to the server-wide default), and a palette or mode name sets it.
    ///     The value is stored on the caller's account, not in the configuration file. Returns 400
    ///     for an unknown name. Requires only a signed-in caller.
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

        // A JSON null removes the override; any other value must name a palette or mode.
        if (
            patch.TryGetMergePatchField("palette", out JsonElement paletteElement)
            && paletteElement.ValueKind == JsonValueKind.Null
        )
            user.PreferredPalette = null;
        else if (patch.TryGetMergePatchEnum("palette", out UiThemePaletteEnum palette))
            user.PreferredPalette = palette;

        if (
            patch.TryGetMergePatchField("mode", out JsonElement modeElement)
            && modeElement.ValueKind == JsonValueKind.Null
        )
            user.PreferredMode = null;
        else if (patch.TryGetMergePatchEnum("mode", out UiThemeModeEnum mode))
            user.PreferredMode = mode;

        IdentityResult result = await userManager.UpdateAsync(user);
        result.ThrowIfFailed();

        return Ok(ToDetail(user));
    }

    /// <summary>
    ///     This method builds <see cref="UserAppearancePreferenceDetail" /> for
    ///     <paramref name="user" />, filling each effective field from the server-wide default when
    ///     the user has no override.
    /// </summary>
    private UserAppearancePreferenceDetail ToDetail(UserEntity user) =>
        new(
            user.PreferredPalette,
            user.PreferredMode,
            user.PreferredPalette ?? appearanceOptions.CurrentValue.Palette,
            user.PreferredMode ?? appearanceOptions.CurrentValue.Mode
        );

    /// <summary>
    ///     This method loads the signed-in caller's <see cref="UserEntity" />, or returns
    ///     <see langword="null" /> when there is none.
    /// </summary>
    private async Task<UserEntity> GetCurrentUserAsync()
    {
        Guid? userId = User.GetUserId();

        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }
}

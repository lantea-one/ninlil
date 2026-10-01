using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Validation;
using Ninlil.Web.Examples.RecursiveSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="RecursiveServerOptions" />: whether recursion
///     and DNSSEC validation are on, query limits and timeouts, cache size, root-hint and
///     trust-anchor overrides, RFC 5011 trust-anchor updates, serve-stale and prefetch. A fresh
///     install starts with recursion off (see
///     <see cref="RecursiveServerOptions.EnableRecursion" />), and this is where it is turned on.
///     Reading needs <c>recursivesettings:read</c> and writing needs
///     <c>recursivesettings:write</c>.
///     <para>
///         <b>Most fields take effect at once, with no restart.</b> The classes that use them on
///         live queries (<c>RecursiveQueryResolver</c>, <c>IterativeResolutionEngine</c>,
///         <c>UpstreamDnsClient</c>, <c>RecursiveCache</c>, <c>TrustAnchorRolloverService</c> and
///         the re-validation path of <c>ForwarderQueryResolver</c>) read
///         <see cref="IOptionsMonitor{TOptions}" /> (see <c>RecursiveQueryResolver</c> for why a
///         singleton needs it). <b>Two fields are exceptions:</b>
///         <see cref="RecursiveServerOptions.RootHintServersOverride" /> is read once by
///         <c>RootHintsPrimingService</c> at startup, and
///         <see cref="RecursiveServerOptions.TrustAnchorsOverride" /> only when
///         <c>FileTrustAnchorStore</c> first seeds its state file and never after.
///         <see cref="Patch" /> returns the note that fits what was changed.
///     </para>
/// </summary>
/// <param name="recursiveOptions">The current recursive resolver settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/recursive-settings")]
public class RecursiveSettingsController(
    IOptionsMonitor<RecursiveServerOptions> recursiveOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the recursive resolver settings as saved. The two override lists
    ///     show the saved value, which is not necessarily the one in use (see the class summary).
    ///     Requires <c>recursivesettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("recursivesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RecursiveSettingsDetailExample))]
    public ActionResult<RecursiveSettingsDetail> Get() =>
        Ok(ToDetail(recursiveOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Recursive</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value; an override list, when present, replaces the
    ///     whole list. The response note says whether a restart is needed. Returns 400 for a
    ///     malformed duration, an invalid root-hint address or a malformed trust anchor. Requires
    ///     <c>recursivesettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("recursivesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(RecursiveSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedHotReloadingField = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchField("enableRecursion", out JsonElement enableRecursion))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableRecursion),
                    enableRecursion.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "enableDnssecValidation",
                out JsonElement enableDnssecValidation
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableDnssecValidation),
                    enableDnssecValidation.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "enableQnameMinimization",
                out JsonElement enableQnameMinimization
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableQnameMinimization),
                    enableQnameMinimization.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            TryGetDuration(
                patch,
                "upstreamQueryTimeout",
                out TimeSpan upstreamQueryTimeout,
                out bool upstreamQueryTimeoutPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.UpstreamQueryTimeout),
                    upstreamQueryTimeout.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (upstreamQueryTimeoutPresent)
            throw new ValidationException(
                "'upstreamQueryTimeout' must be a valid TimeSpan (for example \"00:00:03\")."
            );

        if (
            TryGetDuration(
                patch,
                "overallQueryTimeout",
                out TimeSpan overallQueryTimeout,
                out bool overallQueryTimeoutPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.OverallQueryTimeout),
                    overallQueryTimeout.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (overallQueryTimeoutPresent)
            throw new ValidationException(
                "'overallQueryTimeout' must be a valid TimeSpan (for example \"00:00:15\")."
            );

        if (patch.TryGetMergePatchField("maxReferralDepth", out JsonElement maxReferralDepth))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxReferralDepth),
                    maxReferralDepth.GetInt32()
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("maxCnameChainLength", out JsonElement maxCnameChainLength))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxCnameChainLength),
                    maxCnameChainLength.GetInt32()
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("maxUpstreamQueries", out JsonElement maxUpstreamQueries))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxUpstreamQueries),
                    maxUpstreamQueries.GetInt32()
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("maxCacheEntries", out JsonElement maxCacheEntries))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxCacheEntries),
                    maxCacheEntries.GetInt32()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "rootHintServersOverride",
                out JsonElement rootHintServersOverride
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.RootHintServersOverride),
                    ParseRootHintServers(rootHintServersOverride)
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "trustAnchorsOverride",
                out JsonElement trustAnchorsOverride
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.TrustAnchorsOverride),
                    ParseTrustAnchors(trustAnchorsOverride)
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            patch.TryGetMergePatchField(
                "enableAutomaticTrustAnchorUpdate",
                out JsonElement enableAutomaticTrustAnchorUpdate
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate),
                    enableAutomaticTrustAnchorUpdate.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            TryGetDuration(
                patch,
                "trustAnchorRefreshInterval",
                out TimeSpan trustAnchorRefreshInterval,
                out bool trustAnchorRefreshIntervalPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.TrustAnchorRefreshInterval),
                    trustAnchorRefreshInterval.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (trustAnchorRefreshIntervalPresent)
            throw new ValidationException(
                "'trustAnchorRefreshInterval' must be a valid TimeSpan (for example \"15.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "trustAnchorHoldDownDuration",
                out TimeSpan trustAnchorHoldDownDuration,
                out bool trustAnchorHoldDownDurationPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.TrustAnchorHoldDownDuration),
                    trustAnchorHoldDownDuration.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (trustAnchorHoldDownDurationPresent)
            throw new ValidationException(
                "'trustAnchorHoldDownDuration' must be a valid TimeSpan (for example \"30.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "trustAnchorRemoveHoldDownDuration",
                out TimeSpan trustAnchorRemoveHoldDownDuration,
                out bool trustAnchorRemoveHoldDownDurationPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.TrustAnchorRemoveHoldDownDuration),
                    trustAnchorRemoveHoldDownDuration.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (trustAnchorRemoveHoldDownDurationPresent)
            throw new ValidationException(
                "'trustAnchorRemoveHoldDownDuration' must be a valid TimeSpan (for example \"30.00:00:00\")."
            );

        if (patch.TryGetMergePatchField("enableServeStale", out JsonElement enableServeStale))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableServeStale),
                    enableServeStale.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            TryGetDuration(
                patch,
                "maxStaleServeAge",
                out TimeSpan maxStaleServeAge,
                out bool maxStaleServeAgePresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxStaleServeAge),
                    maxStaleServeAge.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (maxStaleServeAgePresent)
            throw new ValidationException(
                "'maxStaleServeAge' must be a valid TimeSpan (for example \"1.00:00:00\")."
            );

        if (patch.TryGetMergePatchField("enablePrefetch", out JsonElement enablePrefetch))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnablePrefetch),
                    enablePrefetch.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            TryGetDuration(
                patch,
                "prefetchWindow",
                out TimeSpan prefetchWindow,
                out bool prefetchWindowPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.PrefetchWindow),
                    prefetchWindow.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (prefetchWindowPresent)
            throw new ValidationException(
                "'prefetchWindow' must be a valid TimeSpan (for example \"00:00:10\")."
            );

        if (
            TryGetDuration(
                patch,
                "prefetchRecentAccessWindow",
                out TimeSpan prefetchRecentAccessWindow,
                out bool prefetchRecentAccessWindowPresent
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.PrefetchRecentAccessWindow),
                    prefetchRecentAccessWindow.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (prefetchRecentAccessWindowPresent)
            throw new ValidationException(
                "'prefetchRecentAccessWindow' must be a valid TimeSpan (for example \"00:05:00\")."
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        string note = (touchedHotReloadingField, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.MixedReloadNote,
            (false, true) => RuntimeSettingsWriteResult.RestartRequiredNote,
            _ => RuntimeSettingsWriteResult.HotReloadedNote,
        };

        return Ok(new RuntimeSettingsWriteResult(note));
    }

    /// <summary>
    ///     This method reads a merge-patch field that should hold a TimeSpan string, telling the
    ///     caller both whether the field was present and whether it parsed.
    /// </summary>
    private static bool TryGetDuration(
        JsonElement patch,
        string propertyName,
        out TimeSpan value,
        out bool present
    )
    {
        value = default;
        present = patch.TryGetMergePatchField(propertyName, out JsonElement element);

        return present && TimeSpan.TryParse(element.GetString(), out value);
    }

    /// <summary>
    ///     This method parses a <c>rootHintServersOverride</c> field into the shape
    ///     <see cref="RecursiveServerOptions.RootHintServersOverride" /> binds. As RFC 7396 says
    ///     for arrays, it replaces the whole list. Each entry needs a name, and every address must
    ///     be a valid IP address.
    /// </summary>
    private static List<RootHintServerOption> ParseRootHintServers(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException("'rootHintServersOverride' must be a JSON array.");

        List<RootHintServerOption> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("name", out JsonElement nameElement)
                || nameElement.ValueKind != JsonValueKind.String
            )
                throw new ValidationException(
                    "Each 'rootHintServersOverride' entry needs a string 'name'."
                );

            List<string> addresses = [];

            if (item.TryGetProperty("addresses", out JsonElement addressesElement))
            {
                if (addressesElement.ValueKind != JsonValueKind.Array)
                    throw new ValidationException(
                        "Each 'rootHintServersOverride' entry's own 'addresses' must be a JSON array of strings."
                    );

                foreach (JsonElement addressElement in addressesElement.EnumerateArray())
                {
                    string addressText = addressElement.GetString();

                    if (!IPAddress.TryParse(addressText, out _))
                        throw new ValidationException(
                            $"'{addressText}' is not a valid IP address."
                        );

                    addresses.Add(addressText);
                }
            }

            parsed.Add(
                new RootHintServerOption { Name = nameElement.GetString(), Addresses = addresses }
            );
        }

        return parsed;
    }

    /// <summary>
    ///     This method parses a <c>trustAnchorsOverride</c> field into the shape
    ///     <see cref="RecursiveServerOptions.TrustAnchorsOverride" /> binds, checking each entry's
    ///     key tag, algorithm name, digest type and hexadecimal digest.
    /// </summary>
    private static List<TrustAnchorOption> ParseTrustAnchors(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException("'trustAnchorsOverride' must be a JSON array.");

        List<TrustAnchorOption> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ValidationException(
                    "Each 'trustAnchorsOverride' entry must be a JSON object."
                );

            if (
                !item.TryGetProperty("keyTag", out JsonElement keyTagElement)
                || !keyTagElement.TryGetUInt16(out ushort keyTag)
            )
                throw new ValidationException(
                    "Each 'trustAnchorsOverride' entry needs a numeric 'keyTag'."
                );

            if (
                !item.TryGetProperty("algorithm", out JsonElement algorithmElement)
                || !Enum.TryParse(
                    algorithmElement.GetString(),
                    ignoreCase: true,
                    out DnssecAlgorithmEnum algorithm
                )
            )
                throw new ValidationException(
                    "Each 'trustAnchorsOverride' entry needs a valid 'algorithm'. Expected one of: "
                        + $"{string.Join(", ", Enum.GetNames<DnssecAlgorithmEnum>())}."
                );

            if (
                !item.TryGetProperty("digestType", out JsonElement digestTypeElement)
                || !digestTypeElement.TryGetByte(out byte digestType)
            )
                throw new ValidationException(
                    "Each 'trustAnchorsOverride' entry needs a numeric 'digestType'."
                );

            if (
                !item.TryGetProperty("digestHex", out JsonElement digestHexElement)
                || digestHexElement.ValueKind != JsonValueKind.String
            )
                throw new ValidationException(
                    "Each 'trustAnchorsOverride' entry needs a string 'digestHex'."
                );

            string digestHex = digestHexElement.GetString();

            try
            {
                Convert.FromHexString(digestHex);
            }
            catch (FormatException)
            {
                throw new ValidationException($"'{digestHex}' is not valid hexadecimal.");
            }

            parsed.Add(
                new TrustAnchorOption
                {
                    KeyTag = keyTag,
                    Algorithm = algorithm,
                    DigestType = digestType,
                    DigestHex = digestHex,
                }
            );
        }

        return parsed;
    }

    /// <summary>
    ///     This method builds <see cref="RecursiveSettingsDetail" /> from the current
    ///     <see cref="RecursiveServerOptions" />.
    /// </summary>
    private static RecursiveSettingsDetail ToDetail(RecursiveServerOptions options) =>
        new(
            options.EnableRecursion,
            options.EnableDnssecValidation,
            options.EnableQnameMinimization,
            options.UpstreamQueryTimeout,
            options.OverallQueryTimeout,
            options.MaxReferralDepth,
            options.MaxCnameChainLength,
            options.MaxUpstreamQueries,
            options.MaxCacheEntries,
            [
                .. options.RootHintServersOverride.Select(
                    server => new RecursiveRootHintServerDetail(server.Name, server.Addresses)
                ),
            ],
            [
                .. options.TrustAnchorsOverride.Select(anchor => new RecursiveTrustAnchorDetail(
                    anchor.KeyTag,
                    anchor.Algorithm,
                    anchor.DigestType,
                    anchor.DigestHex
                )),
            ],
            options.EnableAutomaticTrustAnchorUpdate,
            options.TrustAnchorRefreshInterval,
            options.TrustAnchorHoldDownDuration,
            options.TrustAnchorRemoveHoldDownDuration,
            options.EnableServeStale,
            options.MaxStaleServeAge,
            options.EnablePrefetch,
            options.PrefetchWindow,
            options.PrefetchRecentAccessWindow
        );
}

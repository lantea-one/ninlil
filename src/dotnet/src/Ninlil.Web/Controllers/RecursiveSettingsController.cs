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
///     This controller reads and writes this host's own <see cref="RecursiveServerOptions" /> --
///     the single highest-priority gap a full REST/UI configuration-completeness audit found: a
///     fresh install starts with recursion off (the correct, documented default -- see
///     <see cref="RecursiveServerOptions.EnableRecursion" />'s own doc comment) and, before this
///     controller existed, had no way to turn it on short of hand-editing the configuration file
///     and restarting. No secret material is involved, so every field is a plain read/write, the
///     same shape <see cref="RateLimitingSettingsController" /> already establishes. Every action
///     requires the <c>recursivesettings:read</c> or <c>recursivesettings:write</c> permission.
///     <para>
///         <b>Most of this class's own fields take effect immediately, with no restart.</b>
///         <see cref="RecursiveServerOptions" /> is read via <see cref="IOptionsMonitor{TOptions}" />
///         everywhere it actually matters for a live query (<c>RecursiveQueryResolver</c>,
///         <c>IterativeResolutionEngine</c>, <c>UpstreamDnsClient</c>, <c>RecursiveCache</c>,
///         <c>TrustAnchorRolloverService</c>, and <c>ForwarderQueryResolver</c>'s own re-validation
///         path) rather than the usual, snapshotted <see cref="IOptions{TOptions}" /> every other
///         pre-<c>AppearanceOptions</c> runtime settings class in this project still uses -- see
///         <c>RecursiveQueryResolver</c>'s own doc comment for why a singleton resolver needs
///         <see cref="IOptionsMonitor{TOptions}" /> specifically to observe a write this
///         controller's own <see cref="Patch" /> action merges into the configuration file.
///         <b>Exactly two fields are the deliberate exception</b>:
///         <see cref="RecursiveServerOptions.RootHintServersOverride" /> and
///         <see cref="RecursiveServerOptions.TrustAnchorsOverride" /> are each read exactly once,
///         either at this host's own startup (<c>RootHintsPrimingService</c>) or the first time
///         <c>FileTrustAnchorStore</c>'s own state file is seeded -- by design, per each
///         property's own doc comment ("this override has no further effect after that first
///         seed") -- so a write touching either one still needs a restart, exactly like every
///         runtime setting in this project predating <c>AppearanceOptions</c>. <see cref="Patch" />
///         reports which of the two notes applies (or a combined one, if a single write touches
///         both kinds of field at once).
///     </para>
/// </summary>
/// <param name="recursiveOptions">This host's own currently-effective recursive resolver settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/recursive-settings")]
public class RecursiveSettingsController(
    IOptionsMonitor<RecursiveServerOptions> recursiveOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live -- reflecting any write
    ///     <see cref="Patch" /> has made to every field except the two override lists, even
    ///     without a restart) recursive resolver settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("recursivesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RecursiveSettingsDetailExample))]
    public ActionResult<RecursiveSettingsDetail> Get() =>
        Ok(ToDetail(recursiveOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Recursive</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. See this class's own doc comment for which fields take effect
    ///     immediately and which need a restart.
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
    ///     This method reads a merge-patch field expected to hold a <see cref="TimeSpan" />-parseable
    ///     string, distinguishing "absent" from "present but unparseable" for the caller.
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
    ///     This method parses a <c>rootHintServersOverride</c> merge-patch field into the exact
    ///     shape <see cref="RecursiveServerOptions.RootHintServersOverride" /> binds, RFC 7396's
    ///     own "arrays replace wholesale, never merge element-by-element" semantics -- validating
    ///     every address as a real IP along the way, the same rigor
    ///     <see cref="Controllers.AppearanceSettingsController.Patch" />'s own enum validation
    ///     already sets for this project's runtime settings controllers.
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
    ///     This method parses a <c>trustAnchorsOverride</c> merge-patch field into the exact shape
    ///     <see cref="RecursiveServerOptions.TrustAnchorsOverride" /> binds, validating each
    ///     entry's own algorithm name and hexadecimal digest along the way.
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
    ///     This method builds <see cref="RecursiveSettingsDetail" /> from a live
    ///     <see cref="RecursiveServerOptions" /> snapshot.
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

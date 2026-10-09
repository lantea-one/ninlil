using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
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
///     trust-anchor overrides, RFC 5011 trust-anchor updates, serve-stale and prefetch, and the
///     list of Insecure domains (negative trust anchors) that skip DNSSEC validation. A fresh
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
    ///     The longest accepted age or window (100 years). These values are added to or
    ///     subtracted from the current time, which overflows for an absurdly large span.
    /// </summary>
    private static readonly TimeSpan AgeMaximum = TimeSpan.FromDays(36500);

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Recursive</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value; an override list, when present, replaces the
    ///     whole list. <c>insecureDomains</c> is the same, and also accepts <c>null</c> to restore the
    ///     built-in list. The response note says whether a restart is needed. Returns 400 for a
    ///     field of the wrong JSON type, an <c>upstreamQueryTimeout</c> outside 100 milliseconds
    ///     to 1 minute, an <c>overallQueryTimeout</c> outside 1 second to 5 minutes or shorter
    ///     than the upstream timeout (the stored value stands in for an absent field), a count
    ///     below 1, a refresh interval or window that is not positive, a hold-down or stale age
    ///     that is negative, a duration beyond the timer limit or 100 years, an invalid root-hint
    ///     address, a malformed trust anchor or an invalid domain name in
    ///     <c>insecureDomains</c>. Requires
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

        if (patch.TryGetMergePatchBoolean("enableRecursion", out bool enableRecursion))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableRecursion),
                    enableRecursion
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchBoolean("enableDnssecValidation", out bool enableDnssecValidation)
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableDnssecValidation),
                    enableDnssecValidation
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchBoolean(
                "enableQnameMinimization",
                out bool enableQnameMinimization
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableQnameMinimization),
                    enableQnameMinimization
                )
            );
            touchedHotReloadingField = true;
        }

        // Both timeouts are cross-checked below, once both are known, because the overall budget
        // must cover at least one upstream attempt. The upstream timeout reaches CancelAfter.
        bool hasUpstreamQueryTimeout = patch.TryGetMergePatchDuration(
            "upstreamQueryTimeout",
            out TimeSpan upstreamQueryTimeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMinutes(1)
        );
        bool hasOverallQueryTimeout = patch.TryGetMergePatchDuration(
            "overallQueryTimeout",
            out TimeSpan overallQueryTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(5)
        );

        if (hasUpstreamQueryTimeout || hasOverallQueryTimeout)
        {
            RecursiveServerOptions current = recursiveOptions.CurrentValue;
            TimeSpan effectiveUpstream = hasUpstreamQueryTimeout
                ? upstreamQueryTimeout
                : current.UpstreamQueryTimeout;
            TimeSpan effectiveOverall = hasOverallQueryTimeout
                ? overallQueryTimeout
                : current.OverallQueryTimeout;

            if (effectiveOverall < effectiveUpstream)
                throw new ValidationException(
                    "'overallQueryTimeout' must not be shorter than 'upstreamQueryTimeout'."
                );
        }

        if (hasUpstreamQueryTimeout)
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

        if (hasOverallQueryTimeout)
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

        if (
            patch.TryGetMergePatchInt32(
                "maxReferralDepth",
                out int maxReferralDepth,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxReferralDepth),
                    maxReferralDepth
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchInt32(
                "maxCnameChainLength",
                out int maxCnameChainLength,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxCnameChainLength),
                    maxCnameChainLength
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchInt32(
                "maxUpstreamQueries",
                out int maxUpstreamQueries,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxUpstreamQueries),
                    maxUpstreamQueries
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchInt32("maxCacheEntries", out int maxCacheEntries, 1, int.MaxValue)
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.MaxCacheEntries),
                    maxCacheEntries
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
            patch.TryGetMergePatchBoolean(
                "enableAutomaticTrustAnchorUpdate",
                out bool enableAutomaticTrustAnchorUpdate
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate),
                    enableAutomaticTrustAnchorUpdate
                )
            );
            touchedHotReloadingField = true;
        }

        // Ages are compared with and subtracted from the current time, so they stop at 100 years.
        if (
            patch.TryGetMergePatchDuration(
                "trustAnchorRefreshInterval",
                out TimeSpan trustAnchorRefreshInterval,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum
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

        if (
            patch.TryGetMergePatchDuration(
                "trustAnchorHoldDownDuration",
                out TimeSpan trustAnchorHoldDownDuration,
                TimeSpan.Zero,
                AgeMaximum
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

        if (
            patch.TryGetMergePatchDuration(
                "trustAnchorRemoveHoldDownDuration",
                out TimeSpan trustAnchorRemoveHoldDownDuration,
                TimeSpan.Zero,
                AgeMaximum
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

        if (patch.TryGetMergePatchBoolean("enableServeStale", out bool enableServeStale))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnableServeStale),
                    enableServeStale
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "maxStaleServeAge",
                out TimeSpan maxStaleServeAge,
                TimeSpan.Zero,
                AgeMaximum
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

        if (patch.TryGetMergePatchBoolean("enablePrefetch", out bool enablePrefetch))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.EnablePrefetch),
                    enablePrefetch
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "prefetchWindow",
                out TimeSpan prefetchWindow,
                TimeSpan.FromTicks(1),
                AgeMaximum
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

        if (
            patch.TryGetMergePatchDuration(
                "prefetchRecentAccessWindow",
                out TimeSpan prefetchRecentAccessWindow,
                TimeSpan.FromTicks(1),
                AgeMaximum
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

        if (patch.TryGetMergePatchField("insecureDomains", out JsonElement insecureDomains))
        {
            // An array replaces the whole list and makes it the operator's own. A JSON null
            // restores the built-in list. An empty list cannot be told from an absent one in
            // configuration, so the "custom" flag is what keeps a deliberately empty list.
            bool restore = insecureDomains.ValueKind == JsonValueKind.Null;

            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.InsecureDomains),
                    restore ? new List<string>() : ParseInsecureDomains(insecureDomains)
                )
            );
            settings.Add(
                new PresetConfigurationSetting(
                    RecursiveServerOptions.SectionName,
                    nameof(RecursiveServerOptions.UseCustomInsecureDomains),
                    !restore
                )
            );
            touchedHotReloadingField = true;
        }

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
    ///     This method parses an <c>insecureDomains</c> field into the list
    ///     <see cref="RecursiveServerOptions.InsecureDomains" /> binds. Each entry must be a valid
    ///     domain name other than the root; it is stored lowercase without a trailing dot, and
    ///     duplicates are dropped, keeping the first.
    /// </summary>
    private static List<string> ParseInsecureDomains(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException(
                "'insecureDomains' must be a JSON array of domain names, or null to restore the built-in list."
            );

        List<string> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            string text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;

            if (
                string.IsNullOrWhiteSpace(text)
                || !DnsName.TryParse(text.Trim(), out DnsName name)
                || name.IsRoot
            )
                throw new ValidationException(
                    $"'{text}' is not a valid domain name for 'insecureDomains'; the root name is not allowed."
                );

            string canonical = name.ToCanonicalLower().ToString().TrimEnd('.');

            // A list entry is a host-style name: letters, digits, hyphens and underscores in
            // each label (a non-ASCII name is entered in its xn-- form). This keeps a typo such
            // as a space from becoming an entry that never matches.
            if (!canonical.Split('.').All(IsHostLabel))
                throw new ValidationException(
                    $"'{text}' is not a valid domain name for 'insecureDomains'; use letters, digits, hyphens and underscores in each label."
                );

            if (!parsed.Contains(canonical))
                parsed.Add(canonical);
        }

        return parsed;
    }

    private static bool IsHostLabel(string label) =>
        label.Length > 0
        && label.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

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
                    string addressText =
                        addressElement.ValueKind == JsonValueKind.String
                            ? addressElement.GetString()
                            : null;

                    if (addressText is null || !IPAddress.TryParse(addressText, out _))
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
                || keyTagElement.ValueKind != JsonValueKind.Number
                || !keyTagElement.TryGetUInt16(out ushort keyTag)
            )
                throw new ValidationException(
                    "Each 'trustAnchorsOverride' entry needs a numeric 'keyTag'."
                );

            if (
                !item.TryGetProperty("algorithm", out JsonElement algorithmElement)
                || algorithmElement.ValueKind != JsonValueKind.String
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
                || digestTypeElement.ValueKind != JsonValueKind.Number
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
            options.PrefetchRecentAccessWindow,
            options.GetEffectiveInsecureDomains(),
            options.UseCustomInsecureDomains,
            RecursiveServerOptions.BuiltInInsecureDomains
        );
}

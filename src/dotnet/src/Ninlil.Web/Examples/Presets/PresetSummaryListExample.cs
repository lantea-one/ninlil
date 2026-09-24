using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Presets;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.PresetsController.List" />'s own <see cref="PresetSummary" /> list.
/// </summary>
public class PresetSummaryListExample : IExamplesProvider<IReadOnlyList<PresetSummary>>
{
    /// <summary>
    ///     This method returns every one of <see cref="DnsServerPresetCatalog" />'s own five
    ///     built-in presets, in catalog order, with their real names, descriptions, and
    ///     configuration-setting/filter-list-subscription counts -- not an invented subset, since
    ///     the real catalog is small and stable enough to show in full.
    /// </summary>
    public IReadOnlyList<PresetSummary> GetExamples() =>
        [
            new PresetSummary(
                "Open",
                "No hardening at all -- today's un-hardened defaults, kept as an explicit, selectable preset so an operator can bulk-revert to it in one action.",
                7,
                0
            ),
            new PresetSummary(
                "Baseline",
                "The currently recommended protocol/transport hardening posture (RIPE-823/KINDNS/NIST SP 800-81 Rev. 3), with no content or threat filtering.",
                9,
                0
            ),
            new PresetSummary(
                "Protective",
                "Baseline, plus a security-only threat-intelligence blocklist (malware/phishing/C2) -- no content filtering, matching CISA/NSA's own \"Protective DNS\" scope.",
                10,
                1
            ),
            new PresetSummary(
                "Family",
                "Protective, plus an adult/explicit-content blocklist, matching CleanBrowsing/OpenDNS FamilyShield's own \"Family Filter\" scope.",
                10,
                2
            ),
            new PresetSummary(
                "FIPS",
                "DNSSEC/TLS/DNSCrypt restricted to FIPS 140-associated policy choices, strict rate limiting, DNS Cookies required. A policy preset, not a validated FIPS 140-3 cryptographic module.",
                11,
                0
            ),
        ];
}

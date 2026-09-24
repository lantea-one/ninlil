using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class contains every built-in <see cref="PresetDefinition" />, drawn from real
///     community/operational DNS-hardening conventions rather than an arbitrary, invented naming
///     scheme.
///     <para>
///         Layering: <see cref="Open" /> is a strict subset of <see cref="Baseline" />, which is
///         a strict subset of <see cref="Protective" />, which is a strict subset of
///         <see cref="Family" />. <see cref="Fips" /> is its own, independent crypto-policy axis
///         -- it starts from <see cref="Baseline" />'s own hardening posture but swaps in its own
///         stricter crypto/DNSCrypt/TLS choices instead of adding a blocklist; it is deliberately
///         not layered on <see cref="Protective" />/<see cref="Family" />.
///     </para>
/// </summary>
public static class DnsServerPresetCatalog
{
    /// <summary>
    ///     This constant contains how often a preset's own subscribed block list is refetched --
    ///     shared by every preset that subscribes to one, since neither <see cref="Protective" />
    ///     nor <see cref="Family" /> has a reason to refresh on a different schedule.
    /// </summary>
    private static readonly TimeSpan SubscribedListRefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    ///     This preset removes every hardening this catalog's other presets can apply -- rate
    ///     limiting off, filtering off, DNS Cookies supported but not required, no DNSSEC
    ///     algorithm or TLS protocol restriction. It is today's own un-hardened defaults, kept as
    ///     its own explicit, selectable preset (rather than folded away as "simply never apply a
    ///     preset") so an operator who applied <see cref="Family" /> or <see cref="Fips" />
    ///     earlier can bulk-*revert* to this in one action, per the confirmed
    ///     "overwrite-then-editable" preset semantics.
    ///     <para>
    ///         Deliberately does <em>not</em> touch <c>DnsServer:Protocol:DnsCrypt:Enable</c>:
    ///         that toggle governs a whole new network listener, a capability decision
    ///         independent of hardening posture (see invariant #6), not a restriction this
    ///         preset needs to remove -- only <see cref="Fips" /> disables it, and only this
    ///         preset resets the crypto-restriction knobs <see cref="Fips" /> itself touches.
    ///     </para>
    /// </summary>
    public static readonly PresetDefinition Open = new(
        "Open",
        "No hardening at all -- today's un-hardened defaults, kept as an explicit, selectable preset so an operator can bulk-revert to it in one action.",
        [
            new PresetConfigurationSetting("DnsServer:RateLimiting", "Enable", false),
            new PresetConfigurationSetting("DnsServer:Cookies", "Enable", true),
            new PresetConfigurationSetting("DnsServer:Cookies", "Require", false),
            new PresetConfigurationSetting("DnsServer:Filtering", "Enable", false),
            new PresetConfigurationSetting(
                "DnsServer:Dnssec",
                "AllowedAlgorithms",
                Array.Empty<string>()
            ),
            new PresetConfigurationSetting(
                "DnsServer:Protocol:DnsOverTls:Certificate",
                "AllowedProtocols",
                "None"
            ),
            new PresetConfigurationSetting(
                "DnsServer:Protocol:DnsOverHttps:Certificate",
                "AllowedProtocols",
                "None"
            ),
        ],
        []
    );

    /// <summary>
    ///     This preset applies the unified, unbranded best-practice posture RIPE-823 ("DNS
    ///     Resolver Recommendations"), the ICANN-affiliated KINDNS "Private Resolver Operators'
    ///     Implementation Guidelines," and NIST SP 800-81 Rev. 3 all converge on: DNSSEC
    ///     validation on (already this project's own default), moderate rate limiting, DNS
    ///     Cookies supported but not required, and the UDP amplification cap at its own defaults
    ///     -- no content or threat filtering of any kind. None of those three
    ///     sources define a named tier themselves; "Baseline" is this project's own name for
    ///     "apply the currently recommended protocol/transport hardening."
    /// </summary>
    public static readonly PresetDefinition Baseline = new(
        "Baseline",
        "The currently recommended protocol/transport hardening posture (RIPE-823/KINDNS/NIST SP 800-81 Rev. 3), with no content or threat filtering.",
        [
            new PresetConfigurationSetting("DnsServer:RateLimiting", "Enable", true),
            new PresetConfigurationSetting("DnsServer:RateLimiting", "QueriesPerSecond", 20),
            new PresetConfigurationSetting("DnsServer:RateLimiting", "BurstSize", 40),
            new PresetConfigurationSetting("DnsServer:Cookies", "Enable", true),
            new PresetConfigurationSetting("DnsServer:Cookies", "Require", false),
            new PresetConfigurationSetting("DnsServer:Pipeline", "MaxUdpAmplificationFactor", 20),
            new PresetConfigurationSetting(
                "DnsServer:Pipeline",
                "MinUdpAmplificationFloorSizeOctets",
                512
            ),
            new PresetConfigurationSetting("DnsServer:Recursive", "EnableDnssecValidation", true),
            new PresetConfigurationSetting("DnsServer:Filtering", "Enable", false),
        ],
        []
    );

    /// <summary>
    ///     This preset is named after CISA/NSA's own "Protective DNS" (PDNS) term of art for a
    ///     resolver that blocks known-malicious domains via threat-intelligence feeds.
    ///     <see cref="Baseline" />'s own settings, plus block/allow-list filtering turned on
    ///     with a security-only threat blocklist attached to the default client group -- source:
    ///     HaGeZi's "TIF" (Threat Intelligence Feeds) medium tier, in its "Wildcard Domains"
    ///     (bare-domain-per-line) format, which <c>DnsFilterListParser</c> reads as bare domains
    ///     even though it carries no <c>0.0.0.0</c> address prefix at all. Deliberately
    ///     blocks nothing else (no ads, no content) -- the same scope discipline Quad9 itself
    ///     holds to.
    /// </summary>
    public static readonly PresetDefinition Protective = new(
        "Protective",
        "Baseline, plus a security-only threat-intelligence blocklist (malware/phishing/C2) -- no content filtering, matching CISA/NSA's own \"Protective DNS\" scope.",
        [
            .. Baseline.ConfigurationSettings,
            new PresetConfigurationSetting("DnsServer:Filtering", "Enable", true),
        ],
        [
            new PresetFilterListSubscription(
                "Ninlil Preset: Protective Threat Feed",
                DnsFilterListKindEnum.Domain,
                DnsFilterListActionEnum.Block,
                "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/tif.medium-onlydomains.txt",
                SubscribedListRefreshInterval
            ),
        ]
    );

    /// <summary>
    ///     This preset is named after the real, established "Family Filter"/"FamilyShield" term
    ///     CleanBrowsing and OpenDNS both already use for this exact category.
    ///     <see cref="Protective" />'s own settings (including its threat blocklist), plus an
    ///     adult/explicit-content blocklist on the same default client group -- source:
    ///     StevenBlack's <c>fakenews-gambling-porn</c> hosts variant, a genuine
    ///     <c>0.0.0.0 domain</c>-per-line hosts file.
    /// </summary>
    public static readonly PresetDefinition Family = new(
        "Family",
        "Protective, plus an adult/explicit-content blocklist, matching CleanBrowsing/OpenDNS FamilyShield's own \"Family Filter\" scope.",
        Protective.ConfigurationSettings,
        [
            .. Protective.FilterListSubscriptions,
            new PresetFilterListSubscription(
                "Ninlil Preset: Family Content Filter",
                DnsFilterListKindEnum.Domain,
                DnsFilterListActionEnum.Block,
                "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/fakenews-gambling-porn/hosts",
                SubscribedListRefreshInterval
            ),
        ]
    );

    /// <summary>
    ///     This preset restricts Ninlil's own configuration surface to policy choices
    ///     commonly associated with FIPS 140 compliance: DNSSEC key generation restricted to
    ///     RSA (SHA-256) and ECDSA P-256/P-384 -- excluding Ed25519/Ed448 (not FIPS-approved
    ///     until FIPS 186-5, and BouncyCastle's implementation is not a validated module
    ///     regardless) and DSA; DNSCrypt disabled entirely (neither Sodium nor BouncyCastle are
    ///     FIPS-validated modules); TLS-based transports restricted to TLS 1.2+; strict rate
    ///     limiting; DNS Cookies required.
    ///     <para>
    ///         <strong>Important disclaimer, kept prominent wherever this preset is
    ///         documented</strong>: applying this preset does <em>not</em>, and cannot, make this
    ///         software a FIPS 140-3 <em>validated cryptographic module</em> -- that requires a
    ///         formal NIST CMVP validation of the actual crypto implementation, which nothing
    ///         this project depends on has. Never let this preset's name imply a compliance
    ///         guarantee it can't back up.
    ///     </para>
    ///     <para>
    ///         Its own independent crypto-policy axis, not layered on <see cref="Protective" />/
    ///         <see cref="Family" />'s content-filtering axis -- see <see cref="DnsServerPresetCatalog" />'s
    ///         own doc comment.
    ///     </para>
    /// </summary>
    public static readonly PresetDefinition Fips = new(
        "FIPS",
        "DNSSEC/TLS/DNSCrypt restricted to FIPS 140-associated policy choices, strict rate limiting, DNS Cookies required. A policy preset, not a validated FIPS 140-3 cryptographic module.",
        [
            new PresetConfigurationSetting("DnsServer:RateLimiting", "Enable", true),
            new PresetConfigurationSetting("DnsServer:RateLimiting", "QueriesPerSecond", 5),
            new PresetConfigurationSetting("DnsServer:RateLimiting", "BurstSize", 10),
            new PresetConfigurationSetting("DnsServer:Cookies", "Enable", true),
            new PresetConfigurationSetting("DnsServer:Cookies", "Require", true),
            new PresetConfigurationSetting("DnsServer:Recursive", "EnableDnssecValidation", true),
            new PresetConfigurationSetting("DnsServer:Filtering", "Enable", false),
            new PresetConfigurationSetting("DnsServer:Protocol:DnsCrypt", "Enable", false),
            new PresetConfigurationSetting(
                "DnsServer:Dnssec",
                "AllowedAlgorithms",
                new[] { "RsaSha256", "EcdsaP256Sha256", "EcdsaP384Sha384" }
            ),
            new PresetConfigurationSetting(
                "DnsServer:Protocol:DnsOverTls:Certificate",
                "AllowedProtocols",
                "Tls12, Tls13"
            ),
            new PresetConfigurationSetting(
                "DnsServer:Protocol:DnsOverHttps:Certificate",
                "AllowedProtocols",
                "Tls12, Tls13"
            ),
        ],
        []
    );

    /// <summary>
    ///     This property contains every built-in preset, in the order they are documented.
    /// </summary>
    public static IReadOnlyList<PresetDefinition> All { get; } =
    [Open, Baseline, Protective, Family, Fips];

    /// <summary>
    ///     This method looks up a built-in preset by its own name, case-insensitively.
    /// </summary>
    /// <param name="name">The preset's own name, for example <c>"Family"</c>.</param>
    /// <param name="preset">The matching preset, if one was found.</param>
    public static bool TryGetByName(string name, out PresetDefinition preset)
    {
        preset = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        return preset is not null;
    }
}

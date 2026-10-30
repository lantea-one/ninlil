using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class contains the built-in <see cref="PresetDefinition" /> values, named after
///     established DNS-hardening and filtering terms. <c>PresetsController</c> lists them and
///     applies one through <see cref="PresetApplier" />.
///     <para>
///         Layering: <see cref="Protective" /> is <see cref="Baseline" /> plus a threat list,
///         and <see cref="Family" /> is <see cref="Protective" /> plus a content list.
///         <see cref="Open" /> turns off what the others turn on and clears the crypto
///         restrictions <see cref="Fips" /> sets. <see cref="Fips" /> is a separate crypto-policy
///         preset: it hardens like <see cref="Baseline" />, more strictly, and restricts
///         DNSSEC, TLS and DNSCrypt instead of adding block lists, so it does not build on
///         <see cref="Protective" /> or <see cref="Family" />.
///     </para>
/// </summary>
public static class DnsServerPresetCatalog
{
    /// <summary>
    ///     This field contains how often a preset's subscribed block lists are fetched again.
    ///     <see cref="Protective" /> and <see cref="Family" /> share it.
    /// </summary>
    private static readonly TimeSpan SubscribedListRefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    ///     This preset turns off the hardening the other presets apply: rate limiting off,
    ///     filtering off, DNS Cookies supported but not required, and no DNSSEC algorithm or TLS
    ///     protocol restriction. These match the shipped defaults. It exists as a preset so an
    ///     operator who applied <see cref="Family" /> or <see cref="Fips" /> can revert in one
    ///     action. It leaves the rate-limit rates, the UDP amplification cap and DNSSEC
    ///     validation as they are.
    ///     <para>
    ///         It does not touch <c>DnsServer:Protocol:DnsCrypt:Enable</c>. That switch turns a
    ///         network listener on or off, which is a capability choice rather than a
    ///         restriction, so reverting hardening must not turn it on. Only <see cref="Fips" />
    ///         sets it.
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
    ///     This preset applies the resolver hardening that RIPE-823 ("DNS Resolver
    ///     Recommendations"), the ICANN-affiliated KINDNS "Private Resolver Operators'
    ///     Implementation Guidelines" and NIST SP 800-81 Rev. 3 agree on: DNSSEC validation on
    ///     (also the default), moderate rate limiting, DNS Cookies supported but not required,
    ///     and the UDP amplification cap at its default values. It does no content or threat
    ///     filtering. None of those sources names a tier; "Baseline" is Ninlil's name for it.
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
    ///     This preset is named after the CISA/NSA "Protective DNS" (PDNS) term for a resolver
    ///     that blocks known-malicious domains using threat-intelligence feeds. It is
    ///     <see cref="Baseline" /> plus filtering turned on, with a security-only threat list
    ///     attached to the default client group: HaGeZi's "TIF" (Threat Intelligence Feeds)
    ///     medium tier in its "Wildcard Domains" format, one bare domain per line, which
    ///     <c>DnsFilterListParser</c> reads as domains. It blocks no ads or content, the same
    ///     scope as Quad9.
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
    ///     This preset is the family-safe tier. It is <see cref="Protective" />, threat list
    ///     included, plus StevenBlack's <c>fakenews-gambling-porn</c> hosts file (one
    ///     <c>0.0.0.0 domain</c> per line) on the same default client group. That file is
    ///     StevenBlack's unified ad and malware list extended with fake-news, gambling and adult
    ///     domains, so it blocks more than adult content.
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
    ///     This preset limits Ninlil's configuration to policy choices commonly associated with
    ///     FIPS 140: DNSSEC algorithms restricted to RSA/SHA-256 and ECDSA P-256/P-384, which
    ///     excludes Ed25519 and Ed448 (not FIPS-approved before FIPS 186-5, and the BouncyCastle
    ///     implementation is not a validated module either way) and DSA; DNSCrypt off (neither
    ///     Sodium nor BouncyCastle is a FIPS-validated module); the TLS transports limited to
    ///     TLS 1.2 and 1.3; strict rate limiting; DNS Cookies required.
    ///     <para>
    ///         <strong>Disclaimer, to keep wherever this preset is documented</strong>: applying
    ///         it does not and cannot make Ninlil a FIPS 140-3 <em>validated cryptographic
    ///         module</em>. That needs a NIST CMVP validation of the crypto implementation, which
    ///         none of Ninlil's dependencies has. The preset's name must never suggest a
    ///         compliance guarantee.
    ///     </para>
    ///     <para>
    ///         It is a crypto-policy preset, not layered on <see cref="Protective" /> or
    ///         <see cref="Family" /> (see <see cref="DnsServerPresetCatalog" />).
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
    ///     This method looks up a built-in preset by name, ignoring case.
    /// </summary>
    /// <param name="name">The preset's name, for example <c>"Family"</c>.</param>
    /// <param name="preset">The matching preset, if one was found.</param>
    public static bool TryGetByName(string name, out PresetDefinition preset)
    {
        preset = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        return preset is not null;
    }
}

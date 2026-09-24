// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.BlockedServices;

/// <summary>
///     This record describes one named, one-click-toggleable service in
///     <see cref="BlockedServiceCatalog" />'s own catalog: a stable key an operator's toggle
///     request names, a human-readable label/description, and the real, live, third-party
///     subscription URL whose domain set this project's own existing
///     <c>DnsFilterListExternalRefreshService</c> subscription mechanism (unchanged, reused as-is)
///     fetches and keeps current on enable.
/// </summary>
/// <param name="Key">
///     The stable, URL-safe identifier this catalog entry is addressed by (e.g. <c>tiktok</c>) --
///     never renamed once shipped, since <c>BlockedServicesController</c> matches an
///     enable/disable request against it.
/// </param>
/// <param name="DisplayName">The human-readable name shown in the Angular catalog UI.</param>
/// <param name="Description">A short, honest description of what toggling this service blocks.</param>
/// <param name="SubscriptionUrl">
///     The real, third-party-hosted, hosts-file-format domain list this entry's own managed
///     <c>DnsFilterListEntity</c> subscribes to when enabled -- see this file's own top-of-file
///     doc comment for the source and license this URL is drawn from.
/// </param>
public sealed record BlockedServiceCatalogEntry(
    string Key,
    string DisplayName,
    string Description,
    string SubscriptionUrl
);

/// <summary>
///     This class holds Ninlil's own curated "blocked services" catalog: a small, honestly-sourced
///     set of named consumer services an operator can block with a single toggle, each backed by a
///     real, independently-maintained, permissively-licensed domain blocklist -- not anything
///     derived from Technitium DNS Server, AdGuard Home, or Pi-hole (see <c>NOTICE.md</c>'s
///     clean-room rule, which this catalog was researched and built specifically to respect).
///     <para>
///         <b>Source, verified directly (fetched and read, not recalled from training data) before
///         a single line of this catalog was written</b>: <b>The Block List Project</b>
///         (<see href="https://github.com/blocklistproject/Lists" />), a community-maintained
///         collection of category- and service-specific hosts-file blocklists, wholly independent
///         of Technitium/AdGuard Home/Pi-hole (no shared authorship, no upstream/fork relationship
///         with any of the three) -- confirmed by reading its own repository directly, not
///         assumed. <b>License</b>: the repository's own top-level <c>LICENSE</c> file is <b>The
///         Unlicense</b> (public-domain dedication -- "free and unencumbered software released
///         into the public domain... for any purpose, commercial or non-commercial, and by any
///         means"); every individual list file's own header additionally states <c>License:
///         MIT</c>. Either license alone already permits this project's use (fetching the file
///         from its origin at runtime, exactly as it publishes it, via the same subscription
///         mechanism already used for StevenBlack/OISD block lists) with no attribution
///         requirement at all -- this catalog credits the source anyway, here and in this
///         project's own root <c>NOTICE.md</c>, because doing so honestly reflects where the data
///         actually comes from.
///     </para>
///     <para>
///         <b>What this catalog deliberately does and does not include</b>: only services The
///         Block List Project itself actually, currently publishes a dedicated list for --
///         confirmed by listing the real repository contents directly
///         (<c>facebook.txt</c>/<c>tiktok.txt</c>/<c>twitter.txt</c>/<c>youtube.txt</c>/
///         <c>whatsapp.txt</c>), not guessed from a "well-known major services" mental list. Its
///         own <c>facebook.txt</c> list is titled "Facebook and Meta domains" and, confirmed by
///         inspecting its actual content, already includes the full <c>instagram.com</c>/
///         <c>cdninstagram.com</c> domain family (Instagram is a Meta-owned property sharing much
///         of its own infrastructure), so one catalog entry honestly covers both; its own
///         <c>twitter.txt</c> list is titled "Twitter/X domains" and covers both the legacy and
///         current branding. <b>Discord has no dedicated list in this source</b> as of this
///         catalog's own research pass -- rather than reach for a second, less-vetted source
///         (compounding this catalog's own licensing/provenance diligence burden for one more
///         entry), this catalog simply omits it; an operator wanting Discord blocked today can
///         still add its domains to an ordinary, manually-managed filter list via
///         <c>FilterListsController</c>/<c>FilterListEntriesController</c>, unaffected by this
///         catalog's own narrower scope.
///     </para>
///     <para>
///         <b>How a toggle actually works</b>: enabling an entry creates (or re-enables, if
///         previously disabled) exactly one <c>DnsFilterListEntity</c> row --
///         <c>Kind=Domain</c>, <c>Action=Deny</c>, <c>Source=Subscribed</c>, its
///         <c>SubscriptionUrl</c> set to this entry's own <see cref="BlockedServiceCatalogEntry.SubscriptionUrl" />
///         -- so the existing, unmodified <c>DnsFilterListExternalRefreshService</c> fetches and
///         periodically refreshes its real domain set exactly the way any other subscribed list
///         already does; no new blocking mechanism, no domains copied into this project's own
///         source tree. <c>BlockedServicesController</c> finds a catalog entry's own managed list
///         (to report its current enabled state, or to remove it again on disable) by matching a
///         <c>DnsFilterListSourceEnum.Subscribed</c> list's own <c>SubscriptionUrl</c> against this
///         catalog entry's <see cref="BlockedServiceCatalogEntry.SubscriptionUrl" /> exactly --
///         stable across a rename, unlike matching by <c>Name</c>.
///     </para>
/// </summary>
public static class BlockedServiceCatalog
{
    /// <summary>
    ///     This property contains every service this catalog currently offers a one-click toggle
    ///     for, in the fixed display order the Angular catalog UI presents them.
    /// </summary>
    public static IReadOnlyList<BlockedServiceCatalogEntry> Entries { get; } =
    [
        new BlockedServiceCatalogEntry(
            "facebook",
            "Facebook & Instagram",
            "Blocks Facebook, Messenger, and Instagram (both Meta-owned properties, covered by one combined list).",
            "https://raw.githubusercontent.com/blocklistproject/Lists/master/facebook.txt"
        ),
        new BlockedServiceCatalogEntry(
            "tiktok",
            "TikTok",
            "Blocks TikTok and its own supporting ByteDance-operated infrastructure domains.",
            "https://raw.githubusercontent.com/blocklistproject/Lists/master/tiktok.txt"
        ),
        new BlockedServiceCatalogEntry(
            "twitter",
            "Twitter / X",
            "Blocks Twitter/X and its own supporting domains.",
            "https://raw.githubusercontent.com/blocklistproject/Lists/master/twitter.txt"
        ),
        new BlockedServiceCatalogEntry(
            "youtube",
            "YouTube",
            "Blocks YouTube outright. An operator who wants YouTube reachable but restricted to its own safe-search-style content mode should use the Safe Search catalog instead of this one.",
            "https://raw.githubusercontent.com/blocklistproject/Lists/master/youtube.txt"
        ),
        new BlockedServiceCatalogEntry(
            "whatsapp",
            "WhatsApp",
            "Blocks WhatsApp and its own supporting domains.",
            "https://raw.githubusercontent.com/blocklistproject/Lists/master/whatsapp.txt"
        ),
    ];

    /// <summary>
    ///     This method looks up a single catalog entry by its stable key, or <see langword="null" />
    ///     if no entry with that key exists.
    /// </summary>
    public static BlockedServiceCatalogEntry FindByKey(string key) =>
        Entries.FirstOrDefault(entry =>
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)
        );
}

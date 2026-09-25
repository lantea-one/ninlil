// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.BlockedServices;

/// <summary>
///     This record describes one service in <see cref="BlockedServiceCatalog" />: a stable key,
///     a label and description for the UI, and the third-party list URL that
///     <see cref="DnsFilterListExternalRefreshService" /> fetches and keeps current once the
///     service is blocked.
/// </summary>
/// <param name="Key">
///     The stable, URL-safe identifier of this entry (e.g. <c>tiktok</c>). It must not be
///     renamed, since <c>BlockedServicesController</c> requests name it.
/// </param>
/// <param name="DisplayName">The human-readable name shown in the UI.</param>
/// <param name="Description">A short description of what blocking this service blocks.</param>
/// <param name="SubscriptionUrl">
///     The third-party, hosts-file-format domain list that this entry's managed
///     <c>DnsFilterListEntity</c> subscribes to (see <see cref="BlockedServiceCatalog" /> for the
///     source and license).
/// </param>
public sealed record BlockedServiceCatalogEntry(
    string Key,
    string DisplayName,
    string Description,
    string SubscriptionUrl
);

/// <summary>
///     This class holds the "blocked services" catalog: a small set of consumer services an
///     operator can block with one toggle, each backed by an independently maintained,
///     permissively licensed domain list. None of it comes from Technitium DNS Server, AdGuard
///     Home or Pi-hole (see the clean-room policy in <c>NOTICE.md</c>).
///     <para>
///         <b>Source.</b> <b>The Block List Project</b>
///         (<see href="https://github.com/blocklistproject/Lists" />), a community-maintained set
///         of category and service hosts-file lists with no authorship or fork relationship to
///         those three projects. <b>License</b>: the repository's <c>LICENSE</c> is <b>The
///         Unlicense</b> (public domain), and each list's header also states
///         <c>License: MIT</c>. Either permits fetching the lists at runtime, as published, with
///         no attribution required; the source is credited here and in <c>NOTICE.md</c> anyway.
///     </para>
///     <para>
///         <b>Scope.</b> Only services the project publishes a dedicated list for are included
///         (<c>facebook.txt</c>, <c>tiktok.txt</c>, <c>twitter.txt</c>, <c>youtube.txt</c>,
///         <c>whatsapp.txt</c>). <c>facebook.txt</c> ("Facebook and Meta domains") includes the
///         <c>instagram.com</c> and <c>cdninstagram.com</c> domains, so one entry covers both, and
///         <c>twitter.txt</c> covers both the Twitter and X names. The source has no Discord
///         list, so Discord is not offered; an operator can block it with an ordinary filter list.
///     </para>
///     <para>
///         <b>How a toggle works.</b> <c>BlockedServicesController</c> serves the catalog.
///         Enabling an entry creates (or re-enables) one <c>DnsFilterListEntity</c> with
///         <c>Kind=Domain</c>, <c>Action=Block</c>, <c>Source=Subscribed</c> and this entry's
///         <see cref="BlockedServiceCatalogEntry.SubscriptionUrl" />, which
///         <see cref="DnsFilterListExternalRefreshService" /> then fetches and refreshes like any
///         other subscribed list. No domains are copied into the source tree. The controller finds
///         an entry's list by matching the subscribed list's <c>SubscriptionUrl</c>, which stays
///         stable if the list is renamed.
///     </para>
/// </summary>
public static class BlockedServiceCatalog
{
    /// <summary>
    ///     This property contains every service in the catalog, in the order the UI shows them.
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

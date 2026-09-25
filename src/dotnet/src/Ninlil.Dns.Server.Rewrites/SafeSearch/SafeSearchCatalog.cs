// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.SafeSearch;

/// <summary>
///     This record describes one <c>domain -&gt; CNAME target</c> pair of a
///     <see cref="SafeSearchCatalogEntry" />. Most engines need several hostnames redirected (the
///     search domain, a mobile variant, an API host) to cover how clients reach the service.
/// </summary>
/// <param name="SourceDomain">The hostname to intercept (e.g. <c>www.google.com</c>).</param>
/// <param name="TargetDomain">
///     The engine's official safe-search hostname that <paramref name="SourceDomain" /> is
///     rewritten to (stored as a CNAME-classified <c>DnsRewriteEntity.Target</c>).
/// </param>
public sealed record SafeSearchRewriteMapping(string SourceDomain, string TargetDomain);

/// <summary>
///     This record describes one Safe Search engine that <see cref="SafeSearchCatalog" /> offers
///     a one-click toggle for.
/// </summary>
/// <param name="Key">The stable, URL-safe identifier this entry is addressed by (e.g. <c>google</c>).</param>
/// <param name="DisplayName">The human-readable engine name shown in the Angular catalog UI.</param>
/// <param name="Description">A short description of what enabling this entry does.</param>
/// <param name="SourceDocumentationUrl">
///     The engine's official documentation page this entry's <see cref="Mappings" /> come
///     from. These targets can change over time, so check this page when updating an entry.
/// </param>
/// <param name="Mappings">Every hostname this entry redirects when enabled.</param>
public sealed record SafeSearchCatalogEntry(
    string Key,
    string DisplayName,
    string Description,
    string SourceDocumentationUrl,
    IReadOnlyList<SafeSearchRewriteMapping> Mappings
);

/// <summary>
///     This class holds the bundled Safe Search (parental-control) catalog: one-click toggles
///     that force safe search network-wide on major search engines, using each engine's
///     published DNS mechanism. The data comes from each engine's first-party documentation, not
///     from another DNS server's list (see the clean-room policy in <c>NOTICE.md</c>).
///     <para>
///         <c>SafeSearchController</c> serves it. Enabling an entry turns each mapping into an
///         ordinary exact-domain, CNAME-classified <c>DnsRewriteEntity</c> row, the same thing an
///         operator could create by hand, so <see cref="DnsRewriteEngine" /> answers it like any
///         other rewrite. The engines' documentation describes the mechanism as "add a CNAME
///         record", which is what these rows produce.
///     </para>
///     <para>
///         <b>Sources</b>, quoted from each engine's documentation:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Google Search</b> -- Google Search Help,
///             <see href="https://support.google.com/websearch/answer/186669" />: "Set the DNS
///             entry for www.google.com (and any other Google country or region domains that your
///             users may use) to be a CNAME for <c>forcesafesearch.google.com</c>." The catalog
///             covers only <c>www.google.com</c> and <c>google.com</c>, not the dozens of
///             <c>google.&lt;cctld&gt;</c> domains; an operator who needs one adds it as an
///             ordinary rewrite.
///         </item>
///         <item>
///             <b>Bing</b> -- Microsoft Support,
///             <see href="https://support.microsoft.com/en-us/topic/block-adult-content-with-safesearch-946059ed-992b-46a0-944a-28e8fb8f1814" />:
///             "Set the DNS entry for www.bing.com to be a CNAME for <c>strict.bing.com</c>,"
///             additionally "set edgeservices.bing.com to be the CNAME for strict.bing.com to
///             force SafeSearch in the Edge sidebar."
///         </item>
///         <item>
///             <b>DuckDuckGo</b> -- DuckDuckGo Help Pages,
///             <see href="https://duckduckgo.com/duckduckgo-help-pages/features/safe-search" />:
///             "Set the DNS entry for duckduckgo.com to the <c>safe.duckduckgo.com</c> CNAME."
///             DuckDuckGo's safe-search domain always enforces the strict level; there is no
///             moderate variant.
///         </item>
///         <item>
///             <b>YouTube Restricted Mode</b> -- Google Workspace Admin Help,
///             <see href="https://support.google.com/a/answer/6214622" />: "To set strict
///             restricted access, set the CNAME value to <c>restrict.youtube.com</c>. To set
///             moderate restricted access, set the CNAME value to
///             <c>restrictmoderate.youtube.com</c>," applied to <c>www.youtube.com</c>,
///             <c>m.youtube.com</c>, <c>youtubei.googleapis.com</c>,
///             <c>youtube.googleapis.com</c>, and <c>www.youtube-nocookie.com</c>. The page warns
///             not to CNAME any other YouTube hostname, since that can break the service.
///             <b>This catalog uses the strict target</b> (<c>restrict.youtube.com</c>), since it
///             is offered as an opt-in parental control. An operator who wants moderate can create
///             those rewrites by hand.
///         </item>
///     </list>
///     <para>
///         <b>Interaction with <see cref="DnsRewriteOptions.Enable" />.</b> Rewrites take effect
///         only while that single global flag is on, and it is off by default. Enabling a Safe
///         Search entry therefore turns the flag on if it was off, which also activates any other
///         rewrites the operator created while it was off. Disabling an entry never turns the
///         flag back off, since other rewrites may depend on it.
///     </para>
/// </summary>
public static class SafeSearchCatalog
{
    /// <summary>
    ///     This property contains every Safe Search engine in the catalog, in the order the UI
    ///     shows them.
    /// </summary>
    public static IReadOnlyList<SafeSearchCatalogEntry> Entries { get; } =
    [
        new SafeSearchCatalogEntry(
            "google",
            "Google Search",
            "Forces Google's own SafeSearch filtering for every client on this network.",
            "https://support.google.com/websearch/answer/186669",
            [
                new SafeSearchRewriteMapping("www.google.com", "forcesafesearch.google.com"),
                new SafeSearchRewriteMapping("google.com", "forcesafesearch.google.com"),
            ]
        ),
        new SafeSearchCatalogEntry(
            "bing",
            "Bing",
            "Forces Bing's own strict SafeSearch filtering, including Microsoft Edge's sidebar search, for every client on this network.",
            "https://support.microsoft.com/en-us/topic/block-adult-content-with-safesearch-946059ed-992b-46a0-944a-28e8fb8f1814",
            [
                new SafeSearchRewriteMapping("www.bing.com", "strict.bing.com"),
                new SafeSearchRewriteMapping("edgeservices.bing.com", "strict.bing.com"),
            ]
        ),
        new SafeSearchCatalogEntry(
            "duckduckgo",
            "DuckDuckGo",
            "Forces DuckDuckGo's own strict safe search for every client on this network.",
            "https://duckduckgo.com/duckduckgo-help-pages/features/safe-search",
            [new SafeSearchRewriteMapping("duckduckgo.com", "safe.duckduckgo.com")]
        ),
        new SafeSearchCatalogEntry(
            "youtube",
            "YouTube Restricted Mode",
            "Forces YouTube's own strict Restricted Mode content filtering for every client on this network.",
            "https://support.google.com/a/answer/6214622",
            [
                new SafeSearchRewriteMapping("www.youtube.com", "restrict.youtube.com"),
                new SafeSearchRewriteMapping("m.youtube.com", "restrict.youtube.com"),
                new SafeSearchRewriteMapping("youtubei.googleapis.com", "restrict.youtube.com"),
                new SafeSearchRewriteMapping("youtube.googleapis.com", "restrict.youtube.com"),
                new SafeSearchRewriteMapping("www.youtube-nocookie.com", "restrict.youtube.com"),
            ]
        ),
    ];

    /// <summary>
    ///     This method looks up a single catalog entry by its stable key, or <see langword="null" />
    ///     if no entry with that key exists.
    /// </summary>
    public static SafeSearchCatalogEntry FindByKey(string key) =>
        Entries.FirstOrDefault(entry =>
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)
        );
}

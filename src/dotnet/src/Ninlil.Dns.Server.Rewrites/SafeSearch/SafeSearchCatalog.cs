// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.SafeSearch;

/// <summary>
///     This record describes one <c>domain -&gt; CNAME target</c> pair a single
///     <see cref="SafeSearchCatalogEntry" /> needs enforced -- most engines need more than one
///     hostname redirected (a plain search domain, a mobile variant, an API host, ...) to actually
///     cover how real clients reach the service.
/// </summary>
/// <param name="SourceDomain">The real-world hostname to intercept (e.g. <c>www.google.com</c>).</param>
/// <param name="TargetDomain">
///     The engine's own official safe-search enforcement hostname <paramref name="SourceDomain" />
///     is rewritten to (a CNAME-classified <c>DnsRewriteEntity.Target</c> once persisted).
/// </param>
public sealed record SafeSearchRewriteMapping(string SourceDomain, string TargetDomain);

/// <summary>
///     This record describes one bundled Safe Search engine <see cref="SafeSearchCatalog" />
///     offers a one-click toggle for.
/// </summary>
/// <param name="Key">The stable, URL-safe identifier this entry is addressed by (e.g. <c>google</c>).</param>
/// <param name="DisplayName">The human-readable engine name shown in the Angular catalog UI.</param>
/// <param name="Description">A short, honest description of what enabling this entry does.</param>
/// <param name="SourceDocumentationUrl">
///     The engine's own official documentation page this entry's <see cref="Mappings" /> were
///     read from -- fetched and read directly before this catalog was written, never guessed or
///     recalled from training data (search-engine safe-search CNAME targets are exactly the kind
///     of detail that quietly changes over time).
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
///     This class holds Ninlil's own bundled Safe Search / parental-control catalog: one-click
///     toggles that force network-wide safe-search enforcement on major search engines/platforms
///     via each engine's own <b>publicly documented, official</b> DNS-based safe-search mechanism
///     -- a real, standard practice each of these providers itself publishes and recommends for
///     exactly this purpose, not anything derived from a competitor DNS server's own curated list
///     (see <c>NOTICE.md</c>'s clean-room rule: this feature's provenance is each search
///     engine's own first-party documentation, which is why the heavier data-source diligence
///     <c>Ninlil.Dns.Server.Filtering.BlockedServices.BlockedServiceCatalog</c> needed does not
///     apply here the same way).
///     <para>
///         Implemented via this project's own existing <c>DnsRewriteEngine</c>
///         (<c>Ninlil.Dns.Server.Rewrites</c>) -- each mapping below becomes one ordinary
///         <c>DnsRewriteEntity</c> row (an exact-domain, CNAME-classified rewrite) once an entry
///         is enabled, exactly the mechanism an operator would configure by hand today; this
///         catalog is sugar over that existing engine, not a second, parallel one. A CNAME-style
///         rewrite is precisely the mechanism each engine's own documentation itself describes
///         (their own examples are phrased as "add a CNAME record"), so this is the correct fit,
///         confirmed by reading <c>DnsRewriteEngine</c>/<c>DnsRewriteTargetClassifier</c> directly
///         before writing this catalog.
///     </para>
///     <para>
///         <b>Sources, each fetched and read directly, verbatim, before being encoded below</b>:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Google Search</b> -- Google Search Help,
///             <see href="https://support.google.com/websearch/answer/186669" />: "Set the DNS
///             entry for www.google.com (and any other Google country or region domains that your
///             users may use) to be a CNAME for <c>forcesafesearch.google.com</c>." This catalog
///             covers <c>www.google.com</c> and bare <c>google.com</c> only -- a deliberate,
///             documented scope cut versus the full country/region-domain list Google's own page
///             also mentions (dozens of <c>google.&lt;cctld&gt;</c> variants), left for an
///             operator to add by hand via the ordinary <c>DnsRewritesController</c> if their own
///             household/network actually resolves one of those regional domains.
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
///             DuckDuckGo's own safe-search domain always enforces its own "strict" level -- there
///             is no separate "moderate" variant to choose between, unlike Google/YouTube.
///         </item>
///         <item>
///             <b>YouTube Restricted Mode</b> -- Google Workspace Admin Help,
///             <see href="https://support.google.com/a/answer/6214622" />: "To set strict
///             restricted access, set the CNAME value to <c>restrict.youtube.com</c>. To set
///             moderate restricted access, set the CNAME value to
///             <c>restrictmoderate.youtube.com</c>," applied to <c>www.youtube.com</c>,
///             <c>m.youtube.com</c>, <c>youtubei.googleapis.com</c>,
///             <c>youtube.googleapis.com</c>, and <c>www.youtube-nocookie.com</c> -- explicitly
///             warning not to CNAME any other YouTube hostname, since doing so can break the
///             service outright. <b>This catalog uses the strict target</b>
///             (<c>restrict.youtube.com</c>), not the moderate one -- a deliberate choice: this
///             whole catalog is framed and offered as a parental-control feature, off by default,
///             an operator opts into deliberately, so the stricter of Google's own two published
///             options is the more defensible default; an operator who specifically wants
///             moderate instead can still hand-configure it via the ordinary rewrites UI, this
///             catalog's own toggle just doesn't offer that second choice today.
///         </item>
///     </list>
///     <para>
///         <b>A real, documented interaction with this project's own single global
///         <c>DnsRewriteOptions.Enable</c> toggle</b>, not something this catalog can route
///         around: rewrites only take effect at all when that flag is on (off by default, per
///         architectural invariant #6), and it is a single switch for <i>every</i> rewrite this
///         server holds, not a per-entry one. Enabling a Safe Search catalog entry therefore also
///         turns that flag on if it was not already -- which, if an operator had earlier created
///         unrelated custom rewrites while deliberately leaving the engine off, makes those go
///         live too, as a side effect of enabling Safe Search. <c>SafeSearchController</c> does
///         not attempt to work around this (there is no honest way to make one feature's toggle
///         partially activate a single boolean flag); disabling a Safe Search entry conversely
///         never turns the flag back off, since doing so could just as easily break an operator's
///         own unrelated rewrites that depend on it staying on. Documented here and in
///         <c>SafeSearchController</c>'s own doc comment -- flagged plainly, per this project's own established
///         "flag the tension explicitly rather than picking silently" convention, rather than
///         silently assumed away.
///     </para>
/// </summary>
public static class SafeSearchCatalog
{
    /// <summary>
    ///     This property contains every Safe Search engine this catalog currently offers a
    ///     one-click toggle for, in the fixed display order the Angular catalog UI presents them.
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

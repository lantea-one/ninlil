using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Web.Examples.FilterPatternPreview;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller previews which of a small sample set of domains a candidate filter-list
///     pattern would actually match -- the live "as I type" preview widget the Angular filter-list
///     entry form shows while an operator is authoring a <see cref="DnsFilterEntryKindEnum.Wildcard" />/
///     <see cref="DnsFilterEntryKindEnum.Regex" /> entry, before it is ever saved.
///     <para>
///         <b>Deliberately calls the exact same compiled-pattern logic the real filter engine
///         uses at query time</b> (<see cref="DnsFilterEngine.CompileOrNull" />/
///         <see cref="DnsFilterEngine.BuildWildcardRegexPattern" />, the same two static helpers
///         <c>FilterListEntriesController</c> itself already calls to validate a pattern before
///         persisting it), rather than a second, JavaScript-side regex engine in the Angular
///         client -- .NET regex and JavaScript's own <c>RegExp</c> are genuinely different
///         dialects (different escape rules, different Unicode/lookbehind support), so a
///         client-only preview could honestly show a match the server would never actually make,
///         or vice versa. This endpoint exists specifically so the preview can never lie about
///         what <see cref="DnsFilterEngine" /> would really do.
///     </para>
///     <para>
///         Read-only and side-effect-free -- no entity is read or written, so this requires only
///         the <c>filterlists:read</c> permission (never <c>filterlists:write</c>), matching the
///         same read/write split every other filtering endpoint already follows.
///     </para>
/// </summary>
[ApiController]
[Route("api/filter-pattern-preview")]
public class FilterPatternPreviewController : ControllerBase
{
    /// <summary>
    ///     This constant contains the small, fixed sample domain set used when a caller supplies
    ///     none of its own -- a deliberately varied mix (an ordinary second-level domain and one
    ///     of its own subdomains, a well-known ad/tracker-style name, a completely unrelated
    ///     domain, and a bare local-network-style name) so a candidate pattern's own real
    ///     specificity is actually visible in the preview, not just "matches everything" or
    ///     "matches nothing."
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSampleDomains =
    [
        "example.com",
        "www.example.com",
        "shop.example.com",
        "ads.example.com",
        "doubleclick.net",
        "googlesyndication.com",
        "youtube.com",
        "www.youtube.com",
        "my-nas.local",
        "unrelated-domain.org",
    ];

    /// <summary>
    ///     This action previews a candidate pattern against a sample domain set. A malformed or
    ///     catastrophic-backtracking pattern can never stall this endpoint -- see
    ///     <see cref="Compile" /> for how the same timeout guard <see cref="DnsFilterEngine" />
    ///     itself uses at query time is reused here.
    /// </summary>
    [HttpPost]
    [RequirePermission("filterlists:read")]
    [SwaggerRequestExample(
        typeof(FilterPatternPreviewRequest),
        typeof(FilterPatternPreviewRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(FilterPatternPreviewResultExample))]
    public ActionResult<FilterPatternPreviewResult> Preview(FilterPatternPreviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Value))
            return Ok(new FilterPatternPreviewResult(false, "A pattern is required.", []));

        IReadOnlyList<string> sampleDomains = request.SampleDomains is { Count: > 0 }
            ? request.SampleDomains
            : DefaultSampleDomains;

        (bool isValid, string error, Func<string, bool> matches) = request.Kind switch
        {
            DnsFilterEntryKindEnum.Wildcard => Compile(
                DnsFilterEngine.BuildWildcardRegexPattern(request.Value),
                "wildcard pattern"
            ),
            DnsFilterEntryKindEnum.Regex => Compile(request.Value, "regular expression"),
            _ => (
                true,
                null,
                (Func<string, bool>)(domain => MatchesDomainEntry(request.Value, domain))
            ),
        };

        if (!isValid)
            return Ok(new FilterPatternPreviewResult(false, error, []));

        List<FilterPatternPreviewMatch> results =
        [
            .. sampleDomains.Select(domain => new FilterPatternPreviewMatch(
                domain,
                matches(NormalizeDomain(domain))
            )),
        ];

        return Ok(new FilterPatternPreviewResult(true, null, results));
    }

    /// <summary>
    ///     This method compiles a regex pattern with this controller's own shared timeout,
    ///     returning a ready-to-use match delegate on success, or a human-readable error naming
    ///     <paramref name="patternKindLabel" /> on failure -- the same
    ///     <see cref="DnsFilterEngine.CompileOrNull" /> compilation the real engine and
    ///     <c>FilterListEntriesController</c>'s own write-time validation both already use, so a
    ///     pattern this preview accepts is guaranteed to also be one the real engine would accept.
    /// </summary>
    private static (bool IsValid, string Error, Func<string, bool> Matches) Compile(
        string pattern,
        string patternKindLabel
    )
    {
        Regex compiled = DnsFilterEngine.CompileOrNull(pattern);

        if (compiled is null)
            return (false, $"Not a valid {patternKindLabel}.", null);

        return (
            true,
            null,
            domain =>
            {
                try
                {
                    return compiled.IsMatch(domain);
                }
                catch (RegexMatchTimeoutException)
                {
                    // Mirrors DnsFilterEngine's own identical timeout handling: a pathological
                    // pattern is treated as "no match" for the domain it timed out on, never
                    // thrown back to the caller -- the same behavior the real engine would show
                    // at query time, which is exactly what this preview promises to never lie
                    // about.
                    return false;
                }
            }
        );
    }

    /// <summary>
    ///     This method mirrors <c>DnsFilterEngine</c>'s own default, non-wildcard/regex matching
    ///     convention for a <see cref="DnsFilterEntryKindEnum.Domain" />-kind entry: a literal
    ///     domain covers itself and every subdomain. Deliberately re-implemented here, in three
    ///     lines, rather than exposed as a third public static helper on <see cref="DnsFilterEngine" />
    ///     purely for this one read-only preview caller -- this project's own established
    ///     precedent (<c>DnsRewriteEngine</c>'s own independently re-implemented wildcard-glob
    ///     translation, rather than a cross-project reference to <c>DnsFilterEngine</c>'s
    ///     identical one) already accepts this exact tradeoff for a genuinely small amount of
    ///     matching logic.
    /// </summary>
    private static bool MatchesDomainEntry(string entryValue, string normalizedQueriedDomain)
    {
        string normalizedEntry = NormalizeDomain(entryValue);

        return normalizedQueriedDomain == normalizedEntry
            || normalizedQueriedDomain.EndsWith("." + normalizedEntry, StringComparison.Ordinal);
    }

    /// <summary>
    ///     This method normalizes a domain to lowercase with no trailing dot, mirroring
    ///     <c>DnsFilterEngine</c>'s own identical normalization.
    /// </summary>
    private static string NormalizeDomain(string value) => value.TrimEnd('.').ToLowerInvariant();
}

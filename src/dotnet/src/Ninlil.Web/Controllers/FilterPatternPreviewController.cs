using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Web.Examples.FilterPatternPreview;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller shows which of a sample set of domains a candidate filter-list pattern would
///     match. The admin console's entry form calls it as the operator types a
///     <see cref="DnsFilterEntryKindEnum.Wildcard" /> or
///     <see cref="DnsFilterEntryKindEnum.Regex" /> entry, before it is saved.
///     <para>
///         <b>It uses the filter engine's own pattern compilation</b>
///         (<see cref="DnsFilterEngine.CompileOrNull" /> and
///         <see cref="DnsFilterEngine.BuildWildcardRegexPattern" />, which
///         <c>FilterListEntriesController</c> also uses to validate entries), not a JavaScript
///         regex in the browser. .NET and JavaScript regex dialects differ (escapes, Unicode,
///         lookbehind), so a browser-side preview could show matches the server would not make, or
///         miss ones it would.
///     </para>
///     <para>
///         It reads and writes nothing, so it needs only <c>filterlists:read</c>.
///     </para>
/// </summary>
[ApiController]
[Route("api/filter-pattern-preview")]
public class FilterPatternPreviewController : ControllerBase
{
    /// <summary>
    ///     This constant contains the sample domains used when the caller supplies none: a mix of a
    ///     plain domain and its subdomains, ad and tracker names, a popular site, a local-network
    ///     name and an unrelated domain, so a pattern's reach is visible rather than "everything"
    ///     or "nothing".
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
    ///     This constant is the most sample domains one preview accepts. It bounds the work and the
    ///     response size of an action that anyone with <c>filterlists:read</c> can call while typing.
    /// </summary>
    public const int MaxSampleDomains = 20;

    /// <summary>
    ///     This constant is the longest sample domain accepted, in characters: 253 is the longest
    ///     domain name in presentation form (RFC 1035 §2.3.4 allows 255 octets on the wire, which
    ///     is 253 characters of text without the root dot).
    /// </summary>
    public const int MaxSampleDomainLength = 253;

    /// <summary>
    ///     This action tests a candidate pattern against the caller's sample domains, or
    ///     <see cref="DefaultSampleDomains" /> when none are given, and reports which match. An
    ///     invalid pattern is reported in the 200 response, not as an error status. Each match is
    ///     bounded by the engine's regex timeout (see <see cref="Compile" />). More than
    ///     <see cref="MaxSampleDomains" /> sample domains, or a sample domain that is null, blank or
    ///     longer than <see cref="MaxSampleDomainLength" /> characters, is rejected with 400.
    ///     Requires <c>filterlists:read</c>.
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

        ValidateSampleDomains(request.SampleDomains);

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
    ///     This method rejects a sample list that is too long or holds an unusable entry, so the
    ///     preview does bounded work and a null entry is a 400 instead of a 500.
    /// </summary>
    /// <exception cref="ValidationException">A rule above is broken.</exception>
    private static void ValidateSampleDomains(IReadOnlyList<string> sampleDomains)
    {
        if (sampleDomains is null)
            return;

        if (sampleDomains.Count > MaxSampleDomains)
            throw new ValidationException(
                $"'sampleDomains' holds at most {MaxSampleDomains} domains."
            );

        foreach (string domain in sampleDomains)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new ValidationException("'sampleDomains' must not hold a blank entry.");

            if (domain.Length > MaxSampleDomainLength)
                throw new ValidationException(
                    $"A sample domain is at most {MaxSampleDomainLength} characters long."
                );
        }
    }

    /// <summary>
    ///     This method compiles a regex pattern with <see cref="DnsFilterEngine.CompileOrNull" />,
    ///     returning a match delegate, or an error naming <paramref name="patternKindLabel" /> when
    ///     it does not compile. The engine and <c>FilterListEntriesController</c> use the same
    ///     compilation, so a pattern accepted here is accepted there.
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
                    // As in DnsFilterEngine, a pattern that times out counts as no match for that
                    // domain rather than an error, so the preview shows what the engine would do.
                    return false;
                }
            }
        );
    }

    /// <summary>
    ///     This method applies <c>DnsFilterEngine</c>'s rule for a
    ///     <see cref="DnsFilterEntryKindEnum.Domain" /> entry: a domain covers itself and every
    ///     subdomain. It is three lines, so it is repeated here rather than exposed as another
    ///     public helper on <see cref="DnsFilterEngine" /> for this one caller.
    /// </summary>
    private static bool MatchesDomainEntry(string entryValue, string normalizedQueriedDomain)
    {
        string normalizedEntry = NormalizeDomain(entryValue);

        return normalizedQueriedDomain == normalizedEntry
            || normalizedQueriedDomain.EndsWith("." + normalizedEntry, StringComparison.Ordinal);
    }

    /// <summary>
    ///     This method lowercases a domain and removes a trailing dot, as <c>DnsFilterEngine</c>
    ///     does.
    /// </summary>
    private static string NormalizeDomain(string value) => value.TrimEnd('.').ToLowerInvariant();
}

using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a request to preview which sample domains a candidate filter list entry
///     pattern would match (<c>POST /api/filter-pattern-preview</c>). The UI sends it while an
///     operator types, before anything is saved.
/// </summary>
/// <param name="Kind">
///     What kind of pattern <see cref="Value" /> is, as a <c>DnsFilterListEntryEntity</c> would
///     store it.
/// </param>
/// <param name="Value">The candidate pattern text.</param>
/// <param name="SampleDomains">
///     The domains to test <see cref="Value" /> against. When omitted or empty, the small built-in
///     set <c>FilterPatternPreviewController.DefaultSampleDomains</c> is used. At most 20 domains
///     are accepted, each non-blank and at most 253 characters; anything else is rejected with 400.
/// </param>
public record FilterPatternPreviewRequest(
    DnsFilterEntryKindEnum Kind,
    string Value,
    IReadOnlyList<string> SampleDomains = null
);

/// <summary>
///     This class carries whether one sample domain matched a previewed pattern.
/// </summary>
public record FilterPatternPreviewMatch(string Domain, bool IsMatch);

/// <summary>
///     This class carries the result of previewing a candidate filter list entry pattern, the
///     response of <c>POST /api/filter-pattern-preview</c>.
/// </summary>
/// <param name="IsValid">
///     Whether <see cref="FilterPatternPreviewRequest.Value" /> compiles as the requested
///     <see cref="FilterPatternPreviewRequest.Kind" />, using the same check
///     <c>FilterListEntriesController</c> applies before saving a
///     <see cref="DnsFilterEntryKindEnum.Wildcard" /> or <see cref="DnsFilterEntryKindEnum.Regex" />
///     entry.
/// </param>
/// <param name="Error">A human-readable reason <see cref="IsValid" /> is <see langword="false" />, or <see langword="null" /> when it is <see langword="true" />.</param>
/// <param name="Matches">
///     Each sample domain tested and whether the pattern matched it. Empty when
///     <see cref="IsValid" /> is <see langword="false" />, since a pattern that does not compile is
///     not tested.
/// </param>
public record FilterPatternPreviewResult(
    bool IsValid,
    string Error,
    IReadOnlyList<FilterPatternPreviewMatch> Matches
);

using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a request to preview which of a sample set of domains a candidate
///     filter-list-entry pattern would match (<c>POST /api/filter-pattern-preview</c>).
/// </summary>
/// <param name="Kind">
///     What kind of pattern <see cref="Value" /> is -- the exact same
///     <see cref="DnsFilterEntryKindEnum" /> a real <c>DnsFilterListEntryEntity</c> row carries.
/// </param>
/// <param name="Value">The candidate pattern text, exactly as an operator is currently typing it.</param>
/// <param name="SampleDomains">
///     The domains to test <see cref="Value" /> against. When omitted or empty, this controller's
///     own small, built-in default sample set is used instead (see
///     <c>FilterPatternPreviewController.DefaultSampleDomains</c>).
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
///     This class carries the result of previewing a candidate filter-list-entry pattern --
///     the response shape for <c>POST /api/filter-pattern-preview</c>.
/// </summary>
/// <param name="IsValid">
///     Whether <see cref="FilterPatternPreviewRequest.Value" /> actually compiles as the
///     requested <see cref="FilterPatternPreviewRequest.Kind" /> -- the exact same compilation
///     <c>FilterListEntriesController</c> itself requires before persisting a
///     <see cref="DnsFilterEntryKindEnum.Wildcard" />/<see cref="DnsFilterEntryKindEnum.Regex" />
///     entry, run here read-only, before the operator has even saved anything.
/// </param>
/// <param name="Error">A human-readable reason <see cref="IsValid" /> is <see langword="false" />, or <see langword="null" /> when it is <see langword="true" />.</param>
/// <param name="Matches">
///     Every sample domain actually tested, and whether the pattern matched it -- empty when
///     <see cref="IsValid" /> is <see langword="false" />, since an uncompilable pattern is never
///     tested against anything.
/// </param>
public record FilterPatternPreviewResult(
    bool IsValid,
    string Error,
    IReadOnlyList<FilterPatternPreviewMatch> Matches
);

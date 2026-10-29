using System.Net;
using System.Text.RegularExpressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class is the in-memory form of one filter list's entries, built once when
///     <see cref="DnsFilterConfigurationCache" /> loads them and then read by every query.
///     <para>
///         A domain list keeps its plain entries in two hash sets (rules and adblock <c>@@</c>
///         exceptions), so <see cref="ContainsDomain" /> looks up the queried name and each of its
///         parent names instead of scanning every entry. That is the same rule as comparing each
///         entry with <c>name == entry || name.EndsWith("." + entry)</c>: an entry covers a name
///         when it equals the name or a suffix of the name that starts at a label boundary.
///         Wildcard and regex entries cannot be hashed, so they stay in <see cref="Patterns" />
///         for the engine to match one by one; each carries its compiled <see cref="Regex" />,
///         built when the index is, so a query compiles nothing. A network list (client-source or response-IP)
///         keeps the entry values for <see cref="ContainsAddress" />.
///     </para>
///     <para>
///         An index is immutable. Memory is about one string plus one hash-set slot per entry,
///         roughly 100 bytes for a typical domain.
///     </para>
/// </summary>
public sealed class DnsFilterListIndex
{
    /// <summary>
    ///     This field is the index of a list with no entries.
    /// </summary>
    public static readonly DnsFilterListIndex Empty = new([], [], [], []);

    /// <summary>
    ///     This field is the rule names of a domain list, normalized.
    /// </summary>
    private readonly HashSet<string> _rules;

    /// <summary>
    ///     This field is the allocation-free view of <see cref="_rules" /> by character span.
    /// </summary>
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _ruleLookup;

    /// <summary>
    ///     This field is the exception names of a domain list, normalized.
    /// </summary>
    private readonly HashSet<string> _exceptions;

    /// <summary>
    ///     This field is the allocation-free view of <see cref="_exceptions" /> by character span.
    /// </summary>
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _exceptionLookup;

    /// <summary>
    ///     This field is the parsed entries of a network list, in list order. An entry that does
    ///     not parse is left out: it never matches an address.
    /// </summary>
    private readonly NetworkEntry[] _networks;

    /// <summary>
    ///     This field is the number of network entries the list had, parsed or not.
    /// </summary>
    private readonly int _networkEntryCount;

    /// <summary>
    ///     This constructor wraps the already built parts.
    /// </summary>
    private DnsFilterListIndex(
        HashSet<string> rules,
        HashSet<string> exceptions,
        DnsFilterPatternEntry[] patterns,
        string[] networkValues
    )
    {
        _rules = rules;
        _ruleLookup = rules.GetAlternateLookup<ReadOnlySpan<char>>();
        _exceptions = exceptions;
        _exceptionLookup = exceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        Patterns = patterns;
        _networkEntryCount = networkValues.Length;
        _networks =
        [
            .. networkValues
                .Select(value =>
                    NetworkEntry.TryParse(value, out NetworkEntry network) ? network : null
                )
                .Where(network => network is not null),
        ];
    }

    /// <summary>
    ///     This property is the wildcard and regex entries of a domain list, in load order.
    /// </summary>
    public IReadOnlyList<DnsFilterPatternEntry> Patterns { get; }

    /// <summary>
    ///     This property is the number of entries the index was built from, for diagnostics.
    /// </summary>
    public int EntryCount => _rules.Count + _exceptions.Count + Patterns.Count + _networkEntryCount;

    /// <summary>
    ///     This method builds the index of one list from its entries.
    /// </summary>
    /// <param name="kind">The list's kind: a domain list is hashed, any other kind keeps address values.</param>
    /// <param name="entries">Every entry of the list.</param>
    /// <param name="previous">
    ///     The index this one replaces, or <see langword="null" />. Its compiled patterns are
    ///     reused for entries with the same kind and text, so a reload of a large list does not
    ///     compile every pattern again.
    /// </param>
    public static DnsFilterListIndex Build(
        DnsFilterListKindEnum kind,
        IReadOnlyList<DnsFilterListEntryEntity> entries,
        DnsFilterListIndex previous = null
    )
    {
        if (kind != DnsFilterListKindEnum.Domain)
            return new DnsFilterListIndex([], [], [], [.. entries.Select(entry => entry.Value)]);

        HashSet<string> rules = new(StringComparer.Ordinal);
        HashSet<string> exceptions = new(StringComparer.Ordinal);
        List<DnsFilterPatternEntry> patterns = [];
        Dictionary<(DnsFilterEntryKindEnum, string), Regex> reusable = [];

        foreach (DnsFilterPatternEntry old in previous?.Patterns ?? [])
        {
            if (old.Compiled is not null)
                reusable[(old.Kind, old.Value)] = old.Compiled;
        }

        foreach (DnsFilterListEntryEntity entry in entries)
        {
            if (entry.Kind is DnsFilterEntryKindEnum.Wildcard or DnsFilterEntryKindEnum.Regex)
            {
                patterns.Add(
                    new DnsFilterPatternEntry(
                        entry.Kind,
                        entry.Value,
                        entry.IsException,
                        reusable.TryGetValue((entry.Kind, entry.Value), out Regex known)
                            ? known
                            : CompilePattern(entry.Kind, entry.Value)
                    )
                );
                continue;
            }

            if (entry.Value is null)
                continue;

            (entry.IsException ? exceptions : rules).Add(NormalizeDomain(entry.Value));
        }

        return new DnsFilterListIndex(rules, exceptions, [.. patterns], []);
    }

    /// <summary>
    ///     This method compiles one wildcard or regex entry the way the engine matches it, or
    ///     returns <see langword="null" /> for a blank pattern or one that does not compile.
    /// </summary>
    private static Regex CompilePattern(DnsFilterEntryKindEnum kind, string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DnsFilterEngine.CompileOrNull(
                kind == DnsFilterEntryKindEnum.Wildcard
                    ? DnsFilterEngine.BuildWildcardRegexPattern(value)
                    : value
            );

    /// <summary>
    ///     This method normalizes a domain to lowercase with no trailing dot, so
    ///     <c>DnsName.ToString</c>'s always-fully-qualified form compares correctly against an
    ///     operator-entered value that may or may not have one.
    /// </summary>
    public static string NormalizeDomain(string value) => value.TrimEnd('.').ToLowerInvariant();

    /// <summary>
    ///     This method says whether a plain entry covers a name: some rule (or exception, with
    ///     <paramref name="exceptions" />) equals the name or one of its parent names.
    /// </summary>
    /// <param name="normalizedName">The queried name, normalized with <see cref="NormalizeDomain" />.</param>
    /// <param name="exceptions">Whether to look in the exception entries instead of the rules.</param>
    public bool ContainsDomain(string normalizedName, bool exceptions)
    {
        if (exceptions ? _exceptions.Count == 0 : _rules.Count == 0)
            return false;

        ReadOnlySpan<char> name = normalizedName;

        while (true)
        {
            if (exceptions ? _exceptionLookup.Contains(name) : _ruleLookup.Contains(name))
                return true;

            // Step to the parent: the part after the next dot. An entry starting inside a label
            // never matches, so only label boundaries are tried.
            int dot = name.IndexOf('.');

            if (dot < 0)
                return false;

            name = name[(dot + 1)..];
        }
    }

    /// <summary>
    ///     This method says whether any entry of a network list contains an address. The entries
    ///     are parsed when the index is built, so a query parses nothing.
    /// </summary>
    /// <param name="address">The address to test.</param>
    public bool ContainsAddress(IPAddress address)
    {
        foreach (NetworkEntry network in _networks)
        {
            if (network.Contains(address))
                return true;
        }

        return false;
    }
}

/// <summary>
///     This record is one wildcard or regex entry of a domain list, as the engine matches it.
/// </summary>
/// <param name="Kind">Either <see cref="DnsFilterEntryKindEnum.Wildcard" /> or <see cref="DnsFilterEntryKindEnum.Regex" />.</param>
/// <param name="Value">The pattern text as stored.</param>
/// <param name="IsException">Whether the entry is an adblock exception.</param>
/// <param name="Compiled">
///     The compiled, case-insensitive regex with the engine's match timeout, or
///     <see langword="null" /> when the pattern is blank or does not compile (such an entry never
///     matches).
/// </param>
public sealed record DnsFilterPatternEntry(
    DnsFilterEntryKindEnum Kind,
    string Value,
    bool IsException,
    Regex Compiled
);

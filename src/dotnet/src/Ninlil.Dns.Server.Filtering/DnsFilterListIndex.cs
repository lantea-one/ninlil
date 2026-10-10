using System.Net;
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
///         for the engine to match one by one. A network list (client-source or response-IP)
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
    ///     This field is the entry values of a network list.
    /// </summary>
    private readonly string[] _networkValues;

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
        _networkValues = networkValues;
    }

    /// <summary>
    ///     This property is the wildcard and regex entries of a domain list, in load order.
    /// </summary>
    public IReadOnlyList<DnsFilterPatternEntry> Patterns { get; }

    /// <summary>
    ///     This property is the number of entries the index was built from, for diagnostics.
    /// </summary>
    public int EntryCount =>
        _rules.Count + _exceptions.Count + Patterns.Count + _networkValues.Length;

    /// <summary>
    ///     This method builds the index of one list from its entries.
    /// </summary>
    /// <param name="kind">The list's kind: a domain list is hashed, any other kind keeps address values.</param>
    /// <param name="entries">Every entry of the list.</param>
    public static DnsFilterListIndex Build(
        DnsFilterListKindEnum kind,
        IReadOnlyList<DnsFilterListEntryEntity> entries
    )
    {
        if (kind != DnsFilterListKindEnum.Domain)
            return new DnsFilterListIndex([], [], [], [.. entries.Select(entry => entry.Value)]);

        HashSet<string> rules = new(StringComparer.Ordinal);
        HashSet<string> exceptions = new(StringComparer.Ordinal);
        List<DnsFilterPatternEntry> patterns = [];

        foreach (DnsFilterListEntryEntity entry in entries)
        {
            if (entry.Kind is DnsFilterEntryKindEnum.Wildcard or DnsFilterEntryKindEnum.Regex)
            {
                patterns.Add(new DnsFilterPatternEntry(entry.Kind, entry.Value, entry.IsException));
                continue;
            }

            if (entry.Value is null)
                continue;

            (entry.IsException ? exceptions : rules).Add(NormalizeDomain(entry.Value));
        }

        return new DnsFilterListIndex(rules, exceptions, [.. patterns], []);
    }

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
    ///     This method says whether any entry of a network list contains an address.
    /// </summary>
    /// <param name="address">The address to test.</param>
    public bool ContainsAddress(IPAddress address)
    {
        foreach (string value in _networkValues)
        {
            if (ClientAddress.NetworkContains(value, address))
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
public sealed record DnsFilterPatternEntry(
    DnsFilterEntryKindEnum Kind,
    string Value,
    bool IsException
);

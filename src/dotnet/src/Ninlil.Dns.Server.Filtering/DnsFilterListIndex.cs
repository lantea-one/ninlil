using System.Collections.Frozen;
using System.Net;
using System.Text;
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
///         for the engine to match; each carries its compiled <see cref="Regex" />, built when
///         the index is, so a query compiles nothing. A trigram index over their necessary
///         literals (<see cref="MatchesAnyPattern{TState}" />) narrows which of them a query has
///         to try. A network list (client-source or response-IP) keeps the entry values for <see cref="ContainsAddress" />.
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
    ///     This field is the ordinary (non-exception) wildcard and regex entries, indexed by
    ///     trigram.
    /// </summary>
    private readonly DnsFilterPatternSet _ordinaryPatterns;

    /// <summary>
    ///     This field is the exception wildcard and regex entries, indexed by trigram.
    /// </summary>
    private readonly DnsFilterPatternSet _exceptionPatterns;

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
        _ordinaryPatterns = DnsFilterPatternSet.Build(patterns.Where(p => !p.IsException));
        _exceptionPatterns = DnsFilterPatternSet.Build(patterns.Where(p => p.IsException));
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
    ///     This method runs a test on the wildcard and regex entries that could match a name, and
    ///     says whether the test accepted one. It tries only the entries whose indexed trigram
    ///     the name contains, plus the entries without a usable literal; for a name that is not
    ///     pure ASCII it tries them all. The test still decides every entry it is given, so the
    ///     answer equals a scan of every entry.
    /// </summary>
    /// <param name="normalizedName">The queried name, normalized with <see cref="NormalizeDomain" />.</param>
    /// <param name="exceptions">Whether to use the exception entries instead of the ordinary ones.</param>
    /// <param name="state">A value handed to <paramref name="test" />, so the test can be a cached static lambda.</param>
    /// <param name="test">
    ///     The decision for one entry: the entry, whether the name is pure ASCII (the condition
    ///     <see cref="DnsFilterPatternEntry.CanMatch" /> needs) and <paramref name="state" />.
    /// </param>
    internal bool MatchesAnyPattern<TState>(
        string normalizedName,
        bool exceptions,
        TState state,
        Func<DnsFilterPatternEntry, bool, TState, bool> test
    ) =>
        (exceptions ? _exceptionPatterns : _ordinaryPatterns).MatchesAny(
            normalizedName,
            state,
            test
        );

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
///     Alongside the regex it carries <see cref="RequiredLiteral" />, a cheap necessary
///     condition the engine tests before running the regex.
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
)
{
    /// <summary>
    ///     This property is a lowercase ASCII text that every name the pattern matches must
    ///     contain, or <see langword="null" /> when none can be read off the pattern safely. It
    ///     is derived from <see cref="Kind" /> and <see cref="Value" /> by
    ///     <see cref="DnsFilterPatternPrefilter" />.
    /// </summary>
    public string RequiredLiteral { get; } = DnsFilterPatternPrefilter.Derive(Kind, Value);

    /// <summary>
    ///     This method says whether the pattern can possibly match a name, without running the
    ///     regex. <see langword="false" /> proves the regex would not match; <see langword="true" />
    ///     proves nothing, so the regex still decides.
    /// </summary>
    /// <param name="normalizedName">The queried name, normalized with <see cref="DnsFilterListIndex.NormalizeDomain" />.</param>
    /// <param name="nameIsAscii">
    ///     Whether <paramref name="normalizedName" /> is pure ASCII. The check only applies then:
    ///     a regex ignoring case also equates some non-ASCII characters with ASCII letters (the
    ///     Kelvin sign with <c>k</c>), which an ordinal search does not.
    /// </param>
    public bool CanMatch(string normalizedName, bool nameIsAscii) =>
        RequiredLiteral is null
        || !nameIsAscii
        || normalizedName.Contains(RequiredLiteral, StringComparison.Ordinal);
}

/// <summary>
///     This class is a set of wildcard or regex entries of one kind (ordinary or exception)
///     that finds the entries a name could match through a trigram index instead of trying each
///     one.
///     <para>
///         An entry whose <see cref="DnsFilterPatternEntry.RequiredLiteral" /> has three or more
///         characters is stored under exactly one trigram of that literal. A name that matches
///         the entry contains the whole literal, so it contains that trigram: looking up the
///         name's trigrams finds every entry the name can match. The trigram of each entry is the
///         one with the fewest entries so far, which keeps the buckets small when many entries
///         share a long common suffix. An entry with no literal, or one shorter than three
///         characters, has no trigram and is always tried. The index only narrows the set: the
///         caller's test (<see cref="DnsFilterPatternEntry.CanMatch" /> and the regex) still
///         decides. A name that is not pure ASCII is tried against every entry, because the
///         literal test is exact only for ASCII names (see
///         <see cref="DnsFilterPatternEntry.CanMatch" />).
///     </para>
///     <para>
///         Entries without a compiled regex never match and are left out. A set is immutable.
///     </para>
/// </summary>
internal sealed class DnsFilterPatternSet
{
    /// <summary>
    ///     This field is the size of the stack buffer that remembers which buckets a name has
    ///     already offered; a name with more distinct hits only loses the duplicate check, never
    ///     correctness.
    /// </summary>
    private const int SeenBufferSize = 256;

    /// <summary>
    ///     This field is the set with no entries.
    /// </summary>
    private static readonly DnsFilterPatternSet Empty = new(
        [],
        [],
        FrozenDictionary<int, DnsFilterPatternEntry[]>.Empty
    );

    /// <summary>
    ///     This field is every entry of the set that can match, in load order.
    /// </summary>
    private readonly DnsFilterPatternEntry[] _all;

    /// <summary>
    ///     This field is the entries without a trigram, tried for every name.
    /// </summary>
    private readonly DnsFilterPatternEntry[] _alwaysCheck;

    /// <summary>
    ///     This field is the entries by their indexed trigram key (see <see cref="TrigramKey" />).
    /// </summary>
    private readonly FrozenDictionary<int, DnsFilterPatternEntry[]> _buckets;

    /// <summary>
    ///     This constructor wraps the built parts.
    /// </summary>
    private DnsFilterPatternSet(
        DnsFilterPatternEntry[] all,
        DnsFilterPatternEntry[] alwaysCheck,
        FrozenDictionary<int, DnsFilterPatternEntry[]> buckets
    )
    {
        _all = all;
        _alwaysCheck = alwaysCheck;
        _buckets = buckets;
    }

    /// <summary>
    ///     This method builds the set from entries of one kind, in load order.
    /// </summary>
    /// <param name="entries">The entries, all ordinary or all exceptions.</param>
    public static DnsFilterPatternSet Build(IEnumerable<DnsFilterPatternEntry> entries)
    {
        List<DnsFilterPatternEntry> all = [];
        List<DnsFilterPatternEntry> alwaysCheck = [];
        List<DnsFilterPatternEntry> indexable = [];
        Dictionary<int, int> frequency = [];

        foreach (DnsFilterPatternEntry entry in entries)
        {
            // An entry without a regex never matches, whatever the name.
            if (entry.Compiled is null)
                continue;

            all.Add(entry);

            string literal = entry.RequiredLiteral;

            if (literal is null || literal.Length < 3)
            {
                alwaysCheck.Add(entry);
                continue;
            }

            indexable.Add(entry);

            for (int i = 0; i + 3 <= literal.Length; i++)
            {
                int key = TrigramKey(literal, i);
                frequency[key] = frequency.GetValueOrDefault(key) + 1;
            }
        }

        // Each entry goes under the rarest trigram of its literal, counted over all literals:
        // a trigram that most entries share (a common domain tail) then holds nothing, so a name
        // that ends in that tail does not drag a large bucket along. Ties go to the bucket with
        // the fewest entries so far, which keeps equally rare trigrams balanced.
        Dictionary<int, List<DnsFilterPatternEntry>> buckets = [];

        foreach (DnsFilterPatternEntry entry in indexable)
        {
            string literal = entry.RequiredLiteral;
            int bestKey = TrigramKey(literal, 0);
            int bestFrequency = frequency[bestKey];
            int bestFill = buckets.TryGetValue(bestKey, out List<DnsFilterPatternEntry> first)
                ? first.Count
                : 0;

            for (int i = 1; i + 3 <= literal.Length; i++)
            {
                int key = TrigramKey(literal, i);
                int keyFrequency = frequency[key];
                int fill = buckets.TryGetValue(key, out List<DnsFilterPatternEntry> list)
                    ? list.Count
                    : 0;

                if (
                    keyFrequency < bestFrequency
                    || (keyFrequency == bestFrequency && fill < bestFill)
                )
                {
                    bestKey = key;
                    bestFrequency = keyFrequency;
                    bestFill = fill;
                }
            }

            if (!buckets.TryGetValue(bestKey, out List<DnsFilterPatternEntry> target))
            {
                target = [];
                buckets[bestKey] = target;
            }

            target.Add(entry);
        }

        if (all.Count == 0)
            return Empty;

        return new DnsFilterPatternSet(
            [.. all],
            [.. alwaysCheck],
            buckets.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray())
        );
    }

    /// <summary>
    ///     This method runs the test on the entries that could match the name and returns
    ///     <see langword="true" /> at the first one the test accepts. Each entry is offered at
    ///     most once, even when its trigram occurs several times in the name.
    /// </summary>
    /// <param name="normalizedName">The queried name, normalized.</param>
    /// <param name="state">A value handed to <paramref name="test" />.</param>
    /// <param name="test">The decision for one entry; see <see cref="DnsFilterListIndex.MatchesAnyPattern{TState}" />.</param>
    public bool MatchesAny<TState>(
        string normalizedName,
        TState state,
        Func<DnsFilterPatternEntry, bool, TState, bool> test
    )
    {
        if (_all.Length == 0)
            return false;

        if (!Ascii.IsValid(normalizedName))
        {
            foreach (DnsFilterPatternEntry entry in _all)
            {
                if (test(entry, false, state))
                    return true;
            }

            return false;
        }

        // A bucket is offered once however often its trigram repeats in the name. The buffer
        // holds only the keys of buckets that exist, which are few.
        Span<int> seen = stackalloc int[SeenBufferSize];
        int seenCount = 0;

        for (int i = 0; i + 3 <= normalizedName.Length; i++)
        {
            int key = TrigramKey(normalizedName, i);

            if (!_buckets.TryGetValue(key, out DnsFilterPatternEntry[] bucket))
                continue;

            if (seen[..seenCount].Contains(key))
                continue;

            if (seenCount < seen.Length)
                seen[seenCount++] = key;

            foreach (DnsFilterPatternEntry entry in bucket)
            {
                if (test(entry, true, state))
                    return true;
            }
        }

        foreach (DnsFilterPatternEntry entry in _alwaysCheck)
        {
            if (test(entry, true, state))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     This method packs the three ASCII characters at an offset into one key, seven bits
    ///     each. The caller guarantees the text is ASCII and has three characters from the offset.
    /// </summary>
    private static int TrigramKey(string text, int offset) =>
        (text[offset] << 14) | (text[offset + 1] << 7) | text[offset + 2];
}

/// <summary>
///     This class derives the necessary literal of a wildcard or regex filter entry, so
///     <see cref="DnsFilterEngine" /> can skip a pattern whose name cannot contain it.
///     <para>
///         The literal is only a precondition: a name that lacks it cannot match, and a name that
///         has it still goes through the regex, which alone decides. So the result of a query is
///         the same as without the prefilter. The derivation is therefore deliberately cautious:
///         wherever the pattern text is not understood with certainty (alternation, groups,
///         non-ASCII text, a numeric or named escape, a nested character class) it returns
///         <see langword="null" /> and the regex runs as before. A literal is always ASCII and
///         lowercase, because the engine compares it with an already lowercased name that is pure
///         ASCII.
///     </para>
/// </summary>
internal static class DnsFilterPatternPrefilter
{
    /// <summary>
    ///     This method derives the necessary literal of one entry, or returns
    ///     <see langword="null" /> for a blank pattern, an unknown kind or a pattern that has none.
    /// </summary>
    /// <param name="kind">The entry kind; only wildcard and regex entries have a literal.</param>
    /// <param name="value">The pattern text as stored.</param>
    public static string Derive(DnsFilterEntryKindEnum kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return kind switch
        {
            DnsFilterEntryKindEnum.Wildcard => LongestWildcardRun(value),
            DnsFilterEntryKindEnum.Regex => LongestRegexRun(value),
            _ => null,
        };
    }

    /// <summary>
    ///     This method returns the longest run of a glob without a wildcard character
    ///     (<c>*</c> or <c>?</c>). Those are the only two characters
    ///     <see cref="DnsFilterEngine.BuildWildcardRegexPattern" /> treats as syntax, so every
    ///     other character matches itself and a run of them must appear in a matching name. A
    ///     non-ASCII character ends a run too, because the regex may match it against an ASCII
    ///     letter.
    /// </summary>
    private static string LongestWildcardRun(string glob)
    {
        RunCollector runs = new();

        foreach (char c in glob)
        {
            if (c is '*' or '?' || c >= 128)
                runs.End();
            else
                runs.Add(c);
        }

        return runs.Finish();
    }

    /// <summary>
    ///     This method returns the longest run of characters a regex must match literally and in
    ///     order, reading the pattern text left to right. Anything it cannot place with
    ///     certainty either ends the current run (a class, a wildcard dot, an anchor, a letter
    ///     escape such as <c>\d</c>) or, for syntax that changes what is required, makes the
    ///     whole pattern give up: alternation, groups, a nested character class, a numeric,
    ///     hexadecimal, Unicode, control, property or named escape.
    ///     A literal followed by <c>?</c>, <c>*</c> or <c>{...}</c> is not required, so it is
    ///     dropped; one followed by <c>+</c> is required but ends the run, because it may repeat.
    /// </summary>
    private static string LongestRegexRun(string pattern)
    {
        RunCollector runs = new();
        bool lastWasLiteral = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            switch (c)
            {
                case '|':
                case '(':
                case ')':
                    return null;

                case '[':
                    i = SkipCharacterClass(pattern, i);

                    if (i < 0)
                        return null;

                    runs.End();
                    lastWasLiteral = false;
                    break;

                case '\\':
                    if (i + 1 >= pattern.Length)
                        return null;

                    char escaped = pattern[++i];

                    if (escaped is 'p' or 'P' or 'x' or 'u' or 'c' or 'k' or (>= '0' and <= '9'))
                        return null;

                    if (char.IsAsciiLetter(escaped) || escaped == '_' || escaped >= 128)
                    {
                        runs.End();
                        lastWasLiteral = false;
                    }
                    else
                    {
                        runs.Add(escaped);
                        lastWasLiteral = true;
                    }

                    break;

                case '*':
                case '?':
                    runs.DropLast(lastWasLiteral);
                    runs.End();
                    lastWasLiteral = false;

                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        i++;

                    break;

                case '+':
                    runs.End();
                    lastWasLiteral = false;

                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        i++;

                    break;

                case '{':
                    int close = pattern.IndexOf('}', i + 1);

                    // Text between braces that is not a count is literal to the regex engine,
                    // and an unescaped | inside it would still be alternation, so only a
                    // real quantifier is skipped.
                    if (close < 0 || !IsQuantifierBody(pattern.AsSpan(i + 1, close - i - 1)))
                        return null;

                    runs.DropLast(lastWasLiteral);
                    runs.End();
                    lastWasLiteral = false;
                    i = close;

                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        i++;

                    break;

                case '^':
                case '$':
                case '.':
                    runs.End();
                    lastWasLiteral = false;
                    break;

                default:
                    if (c >= 128)
                    {
                        runs.End();
                        lastWasLiteral = false;
                    }
                    else
                    {
                        runs.Add(c);
                        lastWasLiteral = true;
                    }

                    break;
            }
        }

        return runs.Finish();
    }

    /// <summary>
    ///     This method says whether the text between the braces of a quantifier is a count:
    ///     digits, then optionally a comma and more digits (<c>2</c>, <c>2,</c>, <c>2,5</c>).
    /// </summary>
    private static bool IsQuantifierBody(ReadOnlySpan<char> body)
    {
        int comma = body.IndexOf(',');
        ReadOnlySpan<char> low = comma < 0 ? body : body[..comma];
        ReadOnlySpan<char> high = comma < 0 ? [] : body[(comma + 1)..];

        return low.Length > 0 && IsAllDigits(low) && IsAllDigits(high);
    }

    /// <summary>
    ///     This method says whether a span holds only ASCII digits (an empty span does).
    /// </summary>
    private static bool IsAllDigits(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     This method returns the index of the <c>]</c> that closes the character class opening
    ///     at <paramref name="open" />, or -1 when the class is one it will not read: a leading
    ///     <c>]</c>, a nested <c>[</c> (subtraction) or no closing bracket.
    /// </summary>
    private static int SkipCharacterClass(string pattern, int open)
    {
        int i = open + 1;

        if (i < pattern.Length && pattern[i] == '^')
            i++;

        if (i < pattern.Length && pattern[i] == ']')
            return -1;

        for (; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\')
                i++;
            else if (pattern[i] == '[')
                return -1;
            else if (pattern[i] == ']')
                return i;
        }

        return -1;
    }

    /// <summary>
    ///     This helper collects runs of required characters and keeps the longest
    ///     (the first on a tie), lowercased. Lowercasing is exact because the literal is ASCII
    ///     and the regex ignores case.
    /// </summary>
    private sealed class RunCollector
    {
        /// <summary>
        ///     This field is the run being built.
        /// </summary>
        private readonly StringBuilder _current = new();

        /// <summary>
        ///     This field is the longest finished run so far.
        /// </summary>
        private string _best = "";

        /// <summary>
        ///     This method appends one ASCII character to the current run, lowercased.
        /// </summary>
        public void Add(char c) => _current.Append(char.ToLowerInvariant(c));

        /// <summary>
        ///     This method removes the last character of the current run when the token just read
        ///     was that character, because a quantifier that follows makes it optional.
        /// </summary>
        public void DropLast(bool lastWasLiteral)
        {
            if (lastWasLiteral && _current.Length > 0)
                _current.Length--;
        }

        /// <summary>
        ///     This method finishes the current run and keeps it when it is the longest.
        /// </summary>
        public void End()
        {
            if (_current.Length > _best.Length)
                _best = _current.ToString();

            _current.Clear();
        }

        /// <summary>
        ///     This method finishes the last run and returns the longest, or
        ///     <see langword="null" /> when no run has a character.
        /// </summary>
        public string Finish()
        {
            End();

            return _best.Length == 0 ? null : _best;
        }
    }
}

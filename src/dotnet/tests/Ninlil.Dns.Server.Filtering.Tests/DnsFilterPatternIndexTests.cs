using System.Text;
using System.Text.RegularExpressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers the trigram index over the wildcard and regex entries of a filter list
///     (<see cref="DnsFilterListIndex.MatchesAnyPattern{TState}" />). The index may only narrow
///     which patterns are tried, so the main checks compare it with the full scan (every pattern
///     of the pass, <see cref="DnsFilterPatternEntry.CanMatch" /> then the plain regex): over
///     hand-written pattern sets with literals shorter than three characters, repeated trigrams,
///     names shorter than three characters, non-ASCII names and exception entries, and over seeded
///     random sets. A counting test shows how few patterns a large list offers for one name.
///     Everything runs in memory with the index's own regex compiler.
/// </summary>
public class DnsFilterPatternIndexTests
{
    /// <summary>
    ///     This type is one entry to put in a list: its kind, pattern text and exception flag.
    /// </summary>
    private readonly record struct Spec(
        DnsFilterEntryKindEnum Kind,
        string Value,
        bool IsException = false
    );

    /// <summary>
    ///     This method builds the index of one domain list from the specs.
    /// </summary>
    private static DnsFilterListIndex BuildIndex(IEnumerable<Spec> specs) =>
        DnsFilterListIndex.Build(
            DnsFilterListKindEnum.Domain,
            [
                .. specs.Select(spec => new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    Kind = spec.Kind,
                    Value = spec.Value,
                    IsException = spec.IsException,
                }),
            ]
        );

    /// <summary>
    ///     This method says whether the plain regex matches the name, treating a match timeout as
    ///     no match as the engine does.
    /// </summary>
    private static bool PlainMatch(DnsFilterPatternEntry entry, string name)
    {
        if (entry.Compiled is null)
            return false;

        try
        {
            return entry.Compiled.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method is the full scan the index replaces: every pattern of the pass, the
    ///     literal test, then the regex.
    /// </summary>
    private static bool FullScan(DnsFilterListIndex index, string name, bool exceptions)
    {
        bool ascii = Ascii.IsValid(name);

        return index.Patterns.Any(pattern =>
            pattern.IsException == exceptions
            && pattern.CanMatch(name, ascii)
            && PlainMatch(pattern, name)
        );
    }

    /// <summary>
    ///     This method asks the index the same question as <see cref="FullScan" />.
    /// </summary>
    private static bool Indexed(DnsFilterListIndex index, string name, bool exceptions) =>
        index.MatchesAnyPattern(
            name,
            exceptions,
            name,
            static (pattern, ascii, state) =>
                pattern.CanMatch(state, ascii) && PlainMatch(pattern, state)
        );

    /// <summary>
    ///     This method asserts that the index and the full scan agree for a name, in both passes.
    /// </summary>
    private static void AssertParity(DnsFilterListIndex index, string rawName)
    {
        string name = DnsFilterListIndex.NormalizeDomain(rawName);

        foreach (bool exceptions in new[] { false, true })
        {
            Assert.True(
                FullScan(index, name, exceptions) == Indexed(index, name, exceptions),
                $"name '{rawName}', exceptions {exceptions}: full scan {FullScan(index, name, exceptions)}, index {Indexed(index, name, exceptions)}"
            );
        }
    }

    /// <summary>
    ///     This field is the hand-written entries: short and exact-length literals, repeated and
    ///     shared trigrams, no literal, a Kelvin-sign case, non-ASCII text, exceptions.
    /// </summary>
    private static readonly Spec[] HandWritten =
    [
        new(DnsFilterEntryKindEnum.Wildcard, "*.ads.example.com"),
        new(DnsFilterEntryKindEnum.Wildcard, "*.ads.example.com", IsException: true),
        new(DnsFilterEntryKindEnum.Wildcard, "ad?.example.net"),
        new(DnsFilterEntryKindEnum.Wildcard, "*ab*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*abc*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*abcabc*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*aaa*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*a?b?c*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*.xyz", IsException: true),
        new(DnsFilterEntryKindEnum.Wildcard, "track?r.*"),
        new(DnsFilterEntryKindEnum.Wildcard, "*.café.example"),
        new(DnsFilterEntryKindEnum.Wildcard, "*kel*"),
        new(DnsFilterEntryKindEnum.Regex, @"^rx5\.list0\.blocked\.test$"),
        new(DnsFilterEntryKindEnum.Regex, @"^rx6\.list0\.blocked\.test$", IsException: true),
        new(DnsFilterEntryKindEnum.Regex, @"^ads?\.tracker\.net$"),
        new(DnsFilterEntryKindEnum.Regex, @"(ads|track)\.example"),
        new(DnsFilterEntryKindEnum.Regex, @"^.*$", IsException: true),
        new(DnsFilterEntryKindEnum.Regex, @"x+"),
        new(DnsFilterEntryKindEnum.Regex, @"^ab$"),
        new(DnsFilterEntryKindEnum.Regex, @"abc"),
        new(DnsFilterEntryKindEnum.Regex, @"kelvin"),
        new(DnsFilterEntryKindEnum.Regex, @"café"),
        new(DnsFilterEntryKindEnum.Regex, @"[invalid"),
        new(DnsFilterEntryKindEnum.Regex, @"(a+)+$"),
    ];

    /// <summary>
    ///     This field is the names to try: shorter than three characters, the empty name, repeated
    ///     trigrams, matches and near misses, mixed case, and non-ASCII names (the Kelvin sign
    ///     included).
    /// </summary>
    private static readonly string[] Names =
    [
        "",
        "a",
        "ab",
        "abc",
        "abcabcabc",
        "aaaaaaaa",
        "aab",
        "x",
        "xyz",
        "a.xyz",
        "www.ads.example.com",
        "ADS.Example.COM",
        "ads.example.com",
        "ad5.example.net",
        "ad.example.net",
        "rx5.list0.blocked.test",
        "rx6.list0.blocked.test",
        "rx5.list0.blocked.tes",
        "ads.tracker.net",
        "ad.tracker.net",
        "track.example",
        "trackxr.example",
        "tracker.org",
        "xxxx",
        "www.safe.example",
        "kelvin.example",
        "Kelvin.example",
        "KELVIN.example",
        "café.example",
        "x.café.example",
        "CAFÉ.example",
        "ïnvalid",
        "[invalid",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    ];

    /// <summary>
    ///     This test checks the hand-written set against every name, one entry at a time and all
    ///     together, so a bucket holding several patterns and a single-pattern index both run.
    /// </summary>
    [Fact]
    public void MatchesAnyPattern_ForHandWrittenSetsAndNames_AgreesWithTheFullScan()
    {
        DnsFilterListIndex all = BuildIndex(HandWritten);

        foreach (string name in Names)
            AssertParity(all, name);

        foreach (Spec spec in HandWritten)
        {
            DnsFilterListIndex single = BuildIndex([spec]);

            foreach (string name in Names)
                AssertParity(single, name);
        }
    }

    /// <summary>
    ///     This test checks a set that puts many patterns under a shared trigram and a name that
    ///     repeats that trigram, so a pattern could be tried twice.
    /// </summary>
    [Fact]
    public void MatchesAnyPattern_ForANameThatRepeatsATrigram_OffersEachPatternOnce()
    {
        DnsFilterListIndex index = BuildIndex(
            Enumerable
                .Range(0, 20)
                .Select(i => new Spec(DnsFilterEntryKindEnum.Wildcard, $"*zzz{i}*"))
        );
        List<string> offered = [];

        bool result = index.MatchesAnyPattern(
            "zzz1zzz1zzz1.zzz2zzz2",
            false,
            offered,
            (pattern, ascii, state) =>
            {
                state.Add(pattern.Value);
                return false;
            }
        );

        Assert.False(result);
        Assert.Equal(offered.Count, offered.Distinct().Count());
    }

    /// <summary>
    ///     This test checks seeded random sets of small patterns against random names, in both
    ///     passes. The alphabet holds literals, wildcard characters and the regex metacharacters
    ///     the literal derivation reads.
    /// </summary>
    [Fact]
    public void MatchesAnyPattern_ForSeededRandomSets_AgreesWithTheFullScan()
    {
        Random random = new(20261030);
        string[] pieces =
        [
            "a",
            "b",
            "c",
            "ab",
            "abc",
            "k",
            ".",
            "\\.",
            "*",
            "?",
            "+",
            "$",
            "^",
            "|",
            "(",
            ")",
            "[a-b]",
            "\\d",
            "-",
            "A",
            "é",
        ];
        string[] nameParts = ["a", "b", "c", "ab", "abc", ".", "-", "A", "k", "K", "é", "1"];

        for (int round = 0; round < 300; round++)
        {
            List<Spec> specs = [];
            int count = random.Next(1, 30);

            for (int i = 0; i < count; i++)
            {
                StringBuilder pattern = new();
                int length = random.Next(1, 7);

                for (int j = 0; j < length; j++)
                    pattern.Append(pieces[random.Next(pieces.Length)]);

                specs.Add(
                    new Spec(
                        random.Next(2) == 0
                            ? DnsFilterEntryKindEnum.Wildcard
                            : DnsFilterEntryKindEnum.Regex,
                        pattern.ToString(),
                        random.Next(4) == 0
                    )
                );
            }

            DnsFilterListIndex index = BuildIndex(specs);

            for (int i = 0; i < 25; i++)
            {
                StringBuilder name = new();
                int length = random.Next(0, 9);

                for (int j = 0; j < length; j++)
                    name.Append(nameParts[random.Next(nameParts.Length)]);

                AssertParity(index, name.ToString());
            }
        }
    }

    /// <summary>
    ///     This test counts the patterns a list shaped like a large benchmark list offers for one
    ///     name (5,000 wildcard and 5,000 regex entries, two of every hundred exceptions): a name
    ///     that matches none offers none of them, and a name that matches one offers a few.
    ///     A full scan offers all of them.
    /// </summary>
    [Fact]
    public void MatchesAnyPattern_ForALargeList_OffersOnlyAFewPatternsForOneName()
    {
        List<Spec> specs = [];

        for (int i = 0; i < 5000; i++)
        {
            specs.Add(
                new Spec(
                    DnsFilterEntryKindEnum.Wildcard,
                    $"*.wild{i * 40}.list0.blocked.test",
                    i % 50 == 7
                )
            );
            specs.Add(
                new Spec(
                    DnsFilterEntryKindEnum.Regex,
                    $"^rx{i * 40 + 1}\\.list0\\.blocked\\.test$",
                    i % 50 == 7
                )
            );
        }

        DnsFilterListIndex index = BuildIndex(specs);
        Assert.Equal(10000, index.Patterns.Count);

        foreach (bool exceptions in new[] { false, true })
        {
            Assert.Equal(0, Offered(index, "www.example.org", exceptions));
            Assert.Equal(0, Offered(index, "cdn.clean.example.org", exceptions));
            Assert.InRange(Offered(index, "x.wild400.list0.blocked.test", exceptions), 0, 30);
            Assert.InRange(Offered(index, "rx4001.list0.blocked.test", exceptions), 0, 30);
        }

        Assert.True(Indexed(index, "x.wild400.list0.blocked.test", exceptions: false));
        Assert.True(Indexed(index, "rx4001.list0.blocked.test", exceptions: false));
        Assert.False(Indexed(index, "x.wild401.list0.blocked.test", exceptions: false));
    }

    /// <summary>
    ///     This method counts the patterns the index offers for a name, accepting none.
    /// </summary>
    private static int Offered(DnsFilterListIndex index, string name, bool exceptions)
    {
        int offered = 0;

        index.MatchesAnyPattern(
            name,
            exceptions,
            0,
            (pattern, ascii, state) =>
            {
                offered++;
                return false;
            }
        );

        return offered;
    }

    /// <summary>
    ///     This test checks that a list with no patterns of a kind offers nothing and matches
    ///     nothing, and that an empty list does too.
    /// </summary>
    [Fact]
    public void MatchesAnyPattern_ForAnEmptyPassOrList_MatchesNothing()
    {
        DnsFilterListIndex onlyOrdinary = BuildIndex([
            new Spec(DnsFilterEntryKindEnum.Wildcard, "*.abc"),
        ]);

        Assert.Equal(0, Offered(onlyOrdinary, "x.abc", exceptions: true));
        Assert.Equal(0, Offered(DnsFilterListIndex.Empty, "x.abc", exceptions: false));
        Assert.False(Indexed(DnsFilterListIndex.Empty, "x.abc", exceptions: true));
    }
}

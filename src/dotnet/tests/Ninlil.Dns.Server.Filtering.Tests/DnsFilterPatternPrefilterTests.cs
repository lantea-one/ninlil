using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.Filtering;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers the necessary-literal prefilter of wildcard and regex filter entries
///     (<see cref="DnsFilterPatternEntry.RequiredLiteral" /> and
///     <see cref="DnsFilterPatternEntry.CanMatch" />). The prefilter may only skip a regex that
///     cannot match, so the main checks compare it with the plain regex scan: over a table of
///     awkward wildcard and regex forms (anchors, alternation, classes, case, escapes, entries
///     with no usable literal), over entries shaped like the benchmark data set, over a seeded
///     random set of small patterns, and through the real engine. The expected literals of a
///     few forms are pinned too, so a derivation that gives up everywhere does not pass by
///     accident. Everything runs in memory with the engine's own regex compiler.
/// </summary>
public class DnsFilterPatternPrefilterTests
{
    /// <summary>
    ///     This field is the wildcard forms the filter accepts, including awkward ones.
    /// </summary>
    private static readonly string[] Wildcards =
    [
        "*.ads.example.com",
        "ads.*.example.com",
        "ad?.example.com",
        "ADS.Example.COM",
        "*",
        "?",
        "**",
        "*?*",
        "*.example.*",
        "*tracker*",
        "track?r.*",
        "a?b?c",
        "*.co.uk",
        "ex+ample.com",
        "a.b(c).d",
        "a|b.example",
        "[abc].example",
        "{1,2}.example",
        @"back\slash.example",
        @"end\*",
        "with space.example",
        "#hash.example",
        "k*",
        "Kelvin.example",
        "café.example",
        "*.é.example",
        "x-y_z.example",
        "$dollar.example",
        "^caret.example",
        "example.com.",
        "*.example.com.",
        "1.2.3.4",
    ];

    /// <summary>
    ///     This field is the regex forms to compare: anchors, alternation, groups, classes,
    ///     quantifiers, escapes of every family, inline options, lookarounds and invalid text.
    /// </summary>
    private static readonly string[] Regexes =
    [
        @"^ads?\.example\.com$",
        @"tracker",
        @"TRACKER",
        @"\.example\.com$",
        @"^ad[0-9]+\.example\.com$",
        @"^ad[0-9]*\.example\.com$",
        @"^[a-z]+\.tracker\.net$",
        @"^[^a]+\.example$",
        @"^[a-z-[aeiou]]+\.example$",
        @"^[]a]+\.example$",
        @"^[\]a]+\.example$",
        @"^(ads|track)\.example\.com$",
        @"(^|\.)tracker\.",
        @"^(?:www\.)?example\.com$",
        @"^(?i)ads\.example\.com$",
        @"^(?-i)ads\.example\.com$",
        @"^(?=ads)ads\.example\.com$",
        @"ads\.example\.com|tracker\.net",
        @"ads|tracker",
        @"^ads+\.example\.com$",
        @"^ads+?\.example\.com$",
        @"^adss*\.example\.com$",
        @"^ad?s\.example\.com$",
        @"^ads??\.example\.com$",
        @"^a{2}d\.example\.com$",
        @"^a{0,2}d\.example\.com$",
        @"^ad{1,}s\.example\.com$",
        @"^ad{s\.example\.com$",
        @"^ad}s\.example\.com$",
        @".*",
        @".+",
        @"^.*$",
        @"^.*\.example\.com$",
        @"^.+\.example\.com$",
        @"\d+\.example\.com",
        @"^\w+\.example\.com$",
        @"^\s*example\.com$",
        @"\bexample\b",
        @"\Bxample",
        @"\Aexample\.com\z",
        @"\x65xample\.com",
        @"example\.com",
        @"\cAexample",
        @"\p{L}+\.example",
        @"\Pl+\.example",
        @"example\.com\1",
        @"\0example",
        @"\k<name>example",
        @"ex\ample\.com",
        @"ex\-ample\.com",
        @"example\/com",
        @"example\_com",
        @"k+",
        @"Kelvin",
        @"café",
        "café",
        "^café\\.example$",
        @"^[é]+\.example$",
        @"example\",
        @"[",
        @"(",
        @"a{",
        @"a{1",
        "",
        " ",
        @"^$",
        @"^",
        @"$",
        @"x*y*z*",
        @"x?y?z?",
        @"xyz+",
        @"x+y+z+",
        @"1\.2\.3\.4",
        @"^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$",
        @"^rx5\.list0\.blocked\.test$",
        @"^(a+)+$",
        @"a.b",
        @"a\.b",
        @"a\\.b",
        @"a\\\.b",
        @"[.]example",
        @"[a-c]xample",
        @"ex[a]mple\.com",
        @"ex[a-z]mple\.com",
    ];

    /// <summary>
    ///     This field is the names to try each entry on: matches, near misses, case, dots,
    ///     digits, a long label, the empty name and non-ASCII names (including the Kelvin sign,
    ///     which a case-insensitive regex equates with <c>k</c>).
    /// </summary>
    private static readonly string[] Names =
    [
        "",
        "a",
        "k",
        "example.com",
        "www.example.com",
        "ads.example.com",
        "ad.example.com",
        "adss.example.com",
        "adsss.example.com",
        "aad.example.com",
        "aaad.example.com",
        "ad5.example.com",
        "ad.5.example.com",
        "ads.tracker.example.com",
        "tracker.net",
        "x.tracker.net",
        "www.tracker.example.com",
        "tracker",
        "trackxr.example",
        "track.r.example",
        "ads.ads.example.com",
        "example.org",
        "example.co.uk",
        "shop.example.co.uk",
        "1.2.3.4",
        "10.20.30.40",
        "xyz",
        "xxyyzz",
        "xy",
        "example_com",
        "example/com",
        "ex-ample.com",
        "exxample.com",
        "example.com1",
        "back\\slash.example",
        "with space.example",
        "#hash.example",
        "$dollar.example",
        "^caret.example",
        "rx5.list0.blocked.test",
        "ads5.list0.blocked.test",
        "ad{s.example.com",
        "ad}s.example.com",
        "a.b",
        "a\\.b",
        "a\\b",
        "aXb",
        "a.b(c).d",
        "a|b.example",
        "aaaa.example",
        "abc.example",
        "xample",
        "ex.ample",
        "café.example",
        "CAFÉ.example",
        "x.é.example",
        "Kelvin.example",
        "K",
        "kK",
        "İstanbul.example",
        "ıstanbul.example",
        new string('a', 63) + ".example.com",
        new string('a', 40) + "b",
    ];

    /// <summary>
    ///     This method compiles one entry the way the index does.
    /// </summary>
    private static DnsFilterPatternEntry EntryFor(DnsFilterEntryKindEnum kind, string value)
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.Domain,
            [
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    Kind = kind,
                    Value = value,
                },
            ]
        );

        return Assert.Single(index.Patterns);
    }

    /// <summary>
    ///     This method says whether the plain scan (no prefilter) matches the name, treating a
    ///     match timeout as no match as the engine does.
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
    ///     This method asserts, for one entry and one name, that the prefiltered result equals
    ///     the plain one: when the prefilter says the pattern cannot match, the regex must not
    ///     match either.
    /// </summary>
    private static void AssertParity(DnsFilterPatternEntry entry, string name)
    {
        string normalized = DnsFilterListIndex.NormalizeDomain(name);
        bool plain = PlainMatch(entry, normalized);
        bool prefiltered =
            entry.CanMatch(normalized, Ascii.IsValid(normalized)) && PlainMatch(entry, normalized);

        Assert.True(
            plain == prefiltered,
            $"{entry.Kind} entry '{entry.Value}' (literal '{entry.RequiredLiteral}') and name '{name}': plain {plain}, prefiltered {prefiltered}"
        );
    }

    /// <summary>
    ///     This test checks every awkward wildcard against every name.
    /// </summary>
    [Fact]
    public void CanMatch_ForEveryAwkwardWildcardAndName_AgreesWithThePlainScan()
    {
        foreach (string wildcard in Wildcards)
        {
            DnsFilterPatternEntry entry = EntryFor(DnsFilterEntryKindEnum.Wildcard, wildcard);

            foreach (string name in Names)
                AssertParity(entry, name);
        }
    }

    /// <summary>
    ///     This test checks every awkward regex against every name.
    /// </summary>
    [Fact]
    public void CanMatch_ForEveryAwkwardRegexAndName_AgreesWithThePlainScan()
    {
        foreach (string regex in Regexes)
        {
            DnsFilterPatternEntry entry = EntryFor(DnsFilterEntryKindEnum.Regex, regex);

            foreach (string name in Names)
                AssertParity(entry, name);
        }
    }

    /// <summary>
    ///     This test checks entries shaped like the benchmark data set's wildcard and regex
    ///     entries against names that match them, share their prefix and are unrelated.
    /// </summary>
    [Fact]
    public void CanMatch_ForGeneratedBenchmarkShapedEntries_AgreesWithThePlainScan()
    {
        List<string> names = [];

        for (int list = 0; list < 3; list++)
        {
            for (int entry = 0; entry < 200; entry += 40)
            {
                names.Add($"x.wild{entry}.list{list}.blocked.test");
                names.Add($"wild{entry}.list{list}.blocked.test");
                names.Add($"rx{entry + 1}.list{list}.blocked.test");
                names.Add($"ads{entry + 2}.list{list}.blocked.test");
            }
        }

        names.Add("www.example.org");
        names.Add("cdn.clean.example.org");

        for (int list = 0; list < 3; list++)
        {
            for (int entry = 0; entry < 200; entry += 40)
            {
                DnsFilterPatternEntry wildcard = EntryFor(
                    DnsFilterEntryKindEnum.Wildcard,
                    $"*.wild{entry}.list{list}.blocked.test"
                );
                DnsFilterPatternEntry regex = EntryFor(
                    DnsFilterEntryKindEnum.Regex,
                    $"^rx{entry + 1}\\.list{list}\\.blocked\\.test$"
                );

                Assert.NotNull(wildcard.RequiredLiteral);
                Assert.NotNull(regex.RequiredLiteral);

                foreach (string name in names)
                {
                    AssertParity(wildcard, name);
                    AssertParity(regex, name);
                }
            }
        }
    }

    /// <summary>
    ///     This test builds a few thousand small patterns from a fixed seed, so the same set runs
    ///     every time, and checks each against a set of names. The alphabet holds literals,
    ///     every metacharacter the derivation reads and a few escapes, so most patterns are
    ///     odd; those that do not compile or whose regex times out count as no match on both
    ///     sides.
    /// </summary>
    [Fact]
    public void CanMatch_ForSeededRandomPatterns_AgreesWithThePlainScan()
    {
        Random random = new(20261029);
        string[] pieces =
        [
            "a",
            "b",
            "c",
            ".",
            "\\.",
            "\\-",
            "\\d",
            "\\w",
            "\\b",
            "*",
            "+",
            "?",
            "{2}",
            "{0,1}",
            "{1,}",
            "[a-b]",
            "[^a]",
            "[.]",
            "(",
            ")",
            "|",
            "^",
            "$",
            "A",
            "B",
            "-",
            "_",
            "1",
            "\\",
            "[",
            "]",
            "{",
            "}",
            "é",
        ];
        string[] randomNames =
        [
            "",
            "a",
            "b",
            "ab",
            "ba",
            "aab",
            "abc",
            "a.b",
            "a-b",
            "a.b.c",
            "ab.c",
            "abc.abc",
            "aa.bb.cc",
            "a1",
            "a_b",
            "b.a.b",
            "cab.cab",
            "aaa",
            "bbb",
            "A.B",
            "1.2",
            "a{2}",
            "é",
            "aé",
        ];

        for (int i = 0; i < 4000; i++)
        {
            StringBuilder pattern = new();
            int length = random.Next(1, 8);

            for (int j = 0; j < length; j++)
                pattern.Append(pieces[random.Next(pieces.Length)]);

            DnsFilterPatternEntry regex = EntryFor(
                DnsFilterEntryKindEnum.Regex,
                pattern.ToString()
            );
            DnsFilterPatternEntry wildcard = EntryFor(
                DnsFilterEntryKindEnum.Wildcard,
                pattern.ToString()
            );

            foreach (string name in randomNames)
            {
                AssertParity(regex, name);
                AssertParity(wildcard, name);
            }
        }
    }

    /// <summary>
    ///     This test pins the literal derived for wildcard forms: the longest run without
    ///     <c>*</c> or <c>?</c>, lowercased, and none when there is no ASCII run.
    /// </summary>
    [Theory]
    [InlineData("*.ads.example.com", ".ads.example.com")]
    [InlineData("ads.*.example.com", ".example.com")]
    [InlineData("ad?.example.com", ".example.com")]
    [InlineData("ADS.Example.COM", "ads.example.com")]
    [InlineData("track?r.*", "track")]
    [InlineData("a?bc?d", "bc")]
    [InlineData("*", null)]
    [InlineData("?*?", null)]
    [InlineData("*é*", null)]
    [InlineData("aébcd", "bcd")]
    public void RequiredLiteral_OfAWildcard_IsTheLongestRunWithoutWildcardCharacters(
        string pattern,
        string expected
    ) => Assert.Equal(expected, EntryFor(DnsFilterEntryKindEnum.Wildcard, pattern).RequiredLiteral);

    /// <summary>
    ///     This test pins the literal derived for regex forms, including the cases that must
    ///     give up (alternation, groups, a nested class, numeric and named escapes) and those
    ///     that drop an optional character.
    /// </summary>
    [Theory]
    [InlineData(@"^rx5\.list0\.blocked\.test$", "rx5.list0.blocked.test")]
    [InlineData(@"tracker", "tracker")]
    [InlineData(@"TRACKER", "tracker")]
    [InlineData(@"\.example\.com$", ".example.com")]
    [InlineData(@"^ads?\.example\.com$", ".example.com")]
    [InlineData(@"^ad[0-9]+\.example\.com$", ".example.com")]
    [InlineData(@"^[a-z]+\.tracker\.net$", ".tracker.net")]
    [InlineData(@"^ads+\.example\.com$", ".example.com")]
    [InlineData(@"^a{2}d\.example\.com$", "d.example.com")]
    [InlineData(@"^.*\.example\.com$", ".example.com")]
    [InlineData(@"ab*cde", "cde")]
    [InlineData(@"ab+c", "ab")]
    [InlineData(@"x\d+y", "x")]
    [InlineData(@"^(ads|track)\.example\.com$", null)]
    [InlineData(@"ads|tracker", null)]
    [InlineData(@"(?i)ads", null)]
    [InlineData(@"(?:www\.)?example\.com", null)]
    [InlineData(@"^[a-z-[aeiou]]+\.example$", null)]
    [InlineData(@"^[]a]+\.example$", null)]
    [InlineData(@"\x65xample", null)]
    [InlineData(@"\p{L}+", null)]
    [InlineData(@"a\1", null)]
    [InlineData(@"a\k<n>", null)]
    [InlineData(@"a{", null)]
    [InlineData(@"{|}\|", null)]
    [InlineData(@"a{x}b", null)]
    [InlineData(@"example\", null)]
    [InlineData(@"[", null)]
    [InlineData(@".*", null)]
    [InlineData(@"^$", null)]
    [InlineData("éé", null)]
    public void RequiredLiteral_OfARegex_IsReadOffOnlyWhenItIsSafe(
        string pattern,
        string expected
    ) => Assert.Equal(expected, EntryFor(DnsFilterEntryKindEnum.Regex, pattern).RequiredLiteral);

    /// <summary>
    ///     This test checks that a domain entry or a blank pattern has no literal.
    /// </summary>
    [Fact]
    public void Derive_ForABlankPatternOrADomainEntry_IsNull()
    {
        Assert.Null(DnsFilterPatternPrefilter.Derive(DnsFilterEntryKindEnum.Regex, " "));
        Assert.Null(DnsFilterPatternPrefilter.Derive(DnsFilterEntryKindEnum.Wildcard, null));
        Assert.Null(DnsFilterPatternPrefilter.Derive(DnsFilterEntryKindEnum.Domain, "example.com"));
    }

    /// <summary>
    ///     This test checks that a name lacking the literal is skipped, that one holding it is
    ///     not, and that a non-ASCII name is never skipped (the regex decides it).
    /// </summary>
    [Fact]
    public void CanMatch_ForANameWithoutTheLiteral_IsFalseExceptForANonAsciiName()
    {
        DnsFilterPatternEntry entry = EntryFor(
            DnsFilterEntryKindEnum.Wildcard,
            "*.tracker.example"
        );

        Assert.False(entry.CanMatch("www.safe.example", nameIsAscii: true));
        Assert.True(entry.CanMatch("www.tracker.example", nameIsAscii: true));
        Assert.True(entry.CanMatch("www.safeé.example", nameIsAscii: false));
    }

    /// <summary>
    ///     This test checks that a pattern with no derivable literal still matches through its
    ///     regex.
    /// </summary>
    [Fact]
    public void CanMatch_ForAPatternWithoutALiteral_IsAlwaysTrueSoTheRegexDecides()
    {
        DnsFilterPatternEntry entry = EntryFor(
            DnsFilterEntryKindEnum.Regex,
            @"^(ads|track)\.test$"
        );

        Assert.Null(entry.RequiredLiteral);
        Assert.True(entry.CanMatch("anything", nameIsAscii: true));
        Assert.True(PlainMatch(entry, "ads.test"));
    }

    /// <summary>
    ///     This test runs every awkward entry through the real engine, one list per entry, and
    ///     checks the decision against the plain regex on a set of names. A regex match that
    ///     times out is a wall-clock event (a stalled thread trips it, not the pattern), so
    ///     the engine's timeout warning marks the evaluation as inconclusive and the name is
    ///     evaluated again; the outcome then depends on the matching, not on the machine's load.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_ForEveryAwkwardEntry_DeniesExactlyWhenThePlainRegexMatches()
    {
        string[] names =
        [
            "ads.example.com",
            "www.tracker.example.com",
            "ad5.example.com",
            "x.wild0.list0.blocked.test",
            "rx5.list0.blocked.test",
            "example.co.uk",
            "tracker.net",
            "xyz",
            "a.b",
            "1.2.3.4",
            "www.safe.example",
        ];

        foreach (
            (DnsFilterEntryKindEnum kind, string pattern) in Wildcards
                .Select(w => (DnsFilterEntryKindEnum.Wildcard, w))
                .Concat(Regexes.Select(r => (DnsFilterEntryKindEnum.Regex, r)))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Item2))
        )
        {
            TimeoutCountingLogger engineLogger = new();
            DnsFilterEngineTests.Fixture fixture = new(engineLogger: engineLogger);
            DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
            DnsFilterListEntity list = fixture.AddListWithKindedEntry(
                "block",
                DnsFilterListActionEnum.Block,
                kind,
                pattern
            );
            fixture.AssignListToGroup(defaultGroup.Id, list.Id);
            DnsFilterPatternEntry reference = EntryFor(kind, pattern);

            foreach (string name in names)
            {
                DnsFilterDecision decision = await EvaluateWithoutTimeoutAsync(
                    fixture,
                    engineLogger,
                    name
                );

                Assert.True(
                    ReferenceMatch(reference, DnsFilterListIndex.NormalizeDomain(name))
                        == decision.IsDenied,
                    $"{kind} entry '{pattern}' and name '{name}': engine denied {decision.IsDenied}"
                );
            }
        }
    }

    /// <summary>
    ///     This test checks the documented timeout rule through the real engine: a regex that
    ///     times out is no match and the engine logs a warning, so the name is allowed. The
    ///     name makes <c>^(a+)+$</c> backtrack exponentially, which outlasts the 250 ms
    ///     timeout on any machine.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_ForARegexThatTimesOut_AllowsTheNameAndLogsAWarning()
    {
        TimeoutCountingLogger engineLogger = new();
        DnsFilterEngineTests.Fixture fixture = new(engineLogger: engineLogger);
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity list = fixture.AddListWithKindedEntry(
            "block",
            DnsFilterListActionEnum.Block,
            DnsFilterEntryKindEnum.Regex,
            @"^(a+)+$"
        );
        fixture.AssignListToGroup(defaultGroup.Id, list.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateDomainAsync(
            DnsName.Parse(new string('a', 40) + "b.test"),
            IPAddress.Parse("10.0.0.5")
        );

        Assert.False(decision.IsDenied);
        Assert.Equal(1, engineLogger.Timeouts);
    }

    /// <summary>
    ///     This method evaluates a name through the engine and repeats it, up to a bound, while
    ///     the engine reports that a regex match timed out, so a stall of the test thread cannot
    ///     change the decision the caller compares.
    /// </summary>
    private static async Task<DnsFilterDecision> EvaluateWithoutTimeoutAsync(
        DnsFilterEngineTests.Fixture fixture,
        TimeoutCountingLogger engineLogger,
        string name
    )
    {
        DnsFilterDecision decision = null;

        for (int attempt = 0; attempt < MaxTimeoutAttempts; attempt++)
        {
            int before = engineLogger.Timeouts;
            decision = await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse(name),
                IPAddress.Parse("10.0.0.5")
            );

            if (engineLogger.Timeouts == before)
                return decision;
        }

        return decision;
    }

    /// <summary>
    ///     This method is the plain regex result for the engine comparison: it matches again when
    ///     the match times out (a stalled thread, not the pattern) and gives up as no match, as
    ///     the engine does, only when every attempt times out.
    /// </summary>
    private static bool ReferenceMatch(DnsFilterPatternEntry entry, string name)
    {
        if (entry.Compiled is null)
            return false;

        for (int attempt = 0; attempt < MaxTimeoutAttempts; attempt++)
        {
            try
            {
                return entry.Compiled.IsMatch(name);
            }
            catch (RegexMatchTimeoutException) { }
        }

        return false;
    }

    /// <summary>
    ///     This field is how often a timed-out match is tried again before it counts as a
    ///     timeout. A pattern that always times out still ends as no match.
    /// </summary>
    private const int MaxTimeoutAttempts = 5;

    /// <summary>
    ///     This class is the logger the engine tests above give the engine. It counts the
    ///     warnings the engine writes when a pattern times out
    ///     (<c>DnsFilterEngine.MatchesPatternEntry</c>) and ignores everything else.
    /// </summary>
    private sealed class TimeoutCountingLogger : ILogger<DnsFilterEngine>
    {
        private int _timeouts;

        /// <summary>
        ///     This property is the number of timeout warnings seen so far.
        /// </summary>
        public int Timeouts => Volatile.Read(ref _timeouts);

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (
                logLevel == LogLevel.Warning
                && formatter(state, exception).Contains("timed out", StringComparison.Ordinal)
            )
                Interlocked.Increment(ref _timeouts);
        }
    }
}

using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers <see cref="DnsFilterListIndex" /> against the rule it replaces: an
///     entry covers a name when the name equals it or ends with a dot followed by it. Every
///     pair of an entry and a name from a table of awkward values must agree with that rule, so
///     the hash lookup cannot change a decision. It also covers the exception set, the pattern
///     entries and the network-list lookup.
/// </summary>
public class DnsFilterListIndexTests
{
    private static readonly string[] Entries =
    [
        "example.com",
        "Example.COM.",
        "ads.example.com",
        "a.b.c.d.example.org",
        "ample.com",
        "com",
        "b..c",
        ".b",
        "x",
        "",
        "tracker.example.net.",
    ];

    private static readonly string[] Names =
    [
        "example.com",
        "www.example.com",
        "wwwexample.com",
        "ads.example.com",
        "deep.ads.example.com",
        "ample.com",
        "xample.com",
        "com",
        "org",
        "a.b.c.d.example.org",
        "z.a.b.c.d.example.org",
        "b.c.d.example.org",
        "a..b..c",
        "a..b",
        "x",
        "y.x",
        "xx",
        "",
        "tracker.example.net",
        "example.net",
    ];

    /// <summary>
    ///     This method is the comparison <see cref="DnsFilterListIndex" /> must agree with.
    /// </summary>
    private static bool Naive(string name, string entry)
    {
        string normalized = DnsFilterListIndex.NormalizeDomain(entry);

        return name == normalized || name.EndsWith("." + normalized, StringComparison.Ordinal);
    }

    private static DnsFilterListEntryEntity Entry(
        string value,
        bool isException = false,
        DnsFilterEntryKindEnum kind = DnsFilterEntryKindEnum.Domain
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Value = value,
            IsException = isException,
            Kind = kind,
        };

    [Fact]
    public void ContainsDomain_ForEveryEntryAndName_AgreesWithTheEndsWithRule()
    {
        foreach (string entry in Entries)
        {
            DnsFilterListIndex index = DnsFilterListIndex.Build(
                DnsFilterListKindEnum.Domain,
                [Entry(entry)]
            );

            foreach (string name in Names)
            {
                string normalizedName = DnsFilterListIndex.NormalizeDomain(name);

                Assert.True(
                    Naive(normalizedName, entry) == index.ContainsDomain(normalizedName, false),
                    $"entry '{entry}' and name '{name}' disagree"
                );
            }
        }
    }

    [Fact]
    public void ContainsDomain_WithManyEntriesAtOnce_AgreesWithTheEndsWithRule()
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.Domain,
            [.. Entries.Select(entry => Entry(entry))]
        );

        foreach (string name in Names)
        {
            string normalizedName = DnsFilterListIndex.NormalizeDomain(name);

            Assert.Equal(
                Entries.Any(entry => Naive(normalizedName, entry)),
                index.ContainsDomain(normalizedName, false)
            );
        }
    }

    [Fact]
    public void ContainsDomain_ForAnExceptionEntry_IsFoundOnlyInTheExceptionSet()
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.Domain,
            [Entry("example.com"), Entry("cdn.example.com", isException: true)]
        );

        Assert.True(index.ContainsDomain("a.example.com", false));
        Assert.False(index.ContainsDomain("a.example.com", true));
        Assert.True(index.ContainsDomain("img.cdn.example.com", true));
    }

    [Fact]
    public void Build_ForWildcardAndRegexEntries_KeepsThemOutOfTheHashSets()
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.Domain,
            [
                Entry("*.ads.*", kind: DnsFilterEntryKindEnum.Wildcard),
                Entry("^x[0-9]+\\.test$", true, DnsFilterEntryKindEnum.Regex),
            ]
        );

        Assert.False(index.ContainsDomain("a.ads.b", false));
        Assert.Equal(2, index.Patterns.Count);
        Assert.False(index.Patterns[0].IsException);
        Assert.True(index.Patterns[1].IsException);
    }

    [Fact]
    public void ContainsAddress_ForANetworkList_MatchesAnyEntry()
    {
        DnsFilterListIndex index = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.ResponseIp,
            [Entry("10.0.0.0/8"), Entry("192.0.2.7")]
        );

        Assert.True(index.ContainsAddress(IPAddress.Parse("10.4.5.6")));
        Assert.True(index.ContainsAddress(IPAddress.Parse("192.0.2.7")));
        Assert.False(index.ContainsAddress(IPAddress.Parse("192.0.2.8")));
    }
}

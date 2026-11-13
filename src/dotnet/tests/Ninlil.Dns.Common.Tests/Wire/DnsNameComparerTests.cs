using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnsNameComparer" /> on in-memory names. The main test runs the
///     reference lookup (a <see cref="DnsName" /> built per suffix and probed in a dictionary with
///     the default name equality) and the suffix lookup over a generated set of names and zones,
///     and requires the same answer for every probe.
/// </summary>
public class DnsNameComparerTests
{
    /// <summary>
    ///     This test checks that the allocation-free suffix probe finds exactly what probing with a
    ///     <see cref="DnsName" /> per suffix finds, over generated names and zones with mixed case,
    ///     the root zone, a zone equal to the query name, sibling zones that share text but not
    ///     labels, escaped dots and maximum-length names.
    /// </summary>
    [Fact]
    public void SuffixLookup_OverGeneratedNamesAndZones_MatchesTheNamePerSuffixLookup()
    {
        Random random = new(20261113);
        string[] alphabet =
        [
            "a",
            "b",
            "ab",
            "example",
            "com",
            "badexample",
            "EXAMPLE",
            "Com",
            @"x\.y",
        ];

        List<string> zoneTexts =
        [
            ".",
            "example.com",
            "badexample.com",
            "com",
            "sub.example.com",
            @"x\.y.com",
            "EXAMPLE.org",
            "a",
            LongName(1),
        ];

        for (int i = 0; i < 60; i++)
            zoneTexts.Add(RandomName(random, alphabet, 1 + random.Next(4)));

        Dictionary<DnsName, int> reference = [];
        Dictionary<DnsName, int> probed = new(DnsNameComparer.Instance);

        for (int i = 0; i < zoneTexts.Count; i++)
        {
            DnsName zone = DnsName.Parse(zoneTexts[i]);
            reference.TryAdd(zone, i);
            probed.TryAdd(zone, i);
        }

        Assert.Equal(reference.Count, probed.Count);

        Dictionary<DnsName, int>.AlternateLookup<DnsNameSuffix> lookup =
            probed.GetAlternateLookup<DnsNameSuffix>();

        List<string> queries =
        [
            ".",
            "example.com",
            "EXAMPLE.COM",
            "www.Example.Com",
            "badexample.com",
            "www.badexample.com",
            "ample.com",
            "xexample.com",
            @"x\.y.com",
            "x.y.com",
            "www.sub.example.com",
            "sub.example.com",
            "example.org",
            "single",
            "a",
            "A",
            LongName(0),
            LongName(1),
            LongName(2),
        ];

        queries.AddRange(zoneTexts);

        for (int i = 0; i < 400; i++)
            queries.Add(RandomName(random, alphabet, 1 + random.Next(6)));

        int hits = 0;

        foreach (string text in queries)
        {
            DnsName name = DnsName.Parse(text);

            for (int skip = 0; skip <= name.Labels.Count; skip++)
            {
                DnsNameSuffix suffix = new(name, skip);
                bool referenceFound = reference.TryGetValue(name.Suffix(skip), out int expected);
                bool found = lookup.TryGetValue(suffix, out int actual);

                Assert.Equal(referenceFound, found);
                Assert.Equal(expected, actual);
                Assert.Equal(
                    name.Suffix(skip).GetHashCode(),
                    DnsNameComparer.Instance.GetHashCode(suffix)
                );

                if (found)
                    hits++;
            }
        }

        // The data set must exercise hits, not only misses.
        Assert.True(hits > 100);
    }

    /// <summary>
    ///     This test checks the cases the lookup must get right: the root, one label, a zone equal
    ///     to the query name, a text-only sibling and an escaped dot.
    /// </summary>
    [Theory]
    [InlineData("example.com", 0, "EXAMPLE.com", true)]
    [InlineData("www.example.com", 1, "Example.COM", true)]
    [InlineData("www.badexample.com", 1, "example.com", false)]
    [InlineData("badexample.com", 0, "example.com", false)]
    [InlineData("www.example.com", 3, ".", true)]
    [InlineData(".", 0, ".", true)]
    [InlineData("com", 0, "com", true)]
    [InlineData("com", 1, ".", true)]
    [InlineData(@"a\.example.com", 1, "com", true)]
    [InlineData(@"a\.example.com", 0, "example.com", false)]
    [InlineData(@"w\.x.com", 1, "com", true)]
    [InlineData(@"w\.x.com", 0, "x.com", false)]
    public void Equals_SuffixAgainstStoredName_FollowsLabelsAndAsciiCase(
        string query,
        int skip,
        string stored,
        bool expected
    )
    {
        DnsName name = DnsName.Parse(query);

        Assert.Equal(
            expected,
            DnsNameComparer.Instance.Equals(new DnsNameSuffix(name, skip), DnsName.Parse(stored))
        );
        Assert.Equal(expected, name.Suffix(skip).Equals(DnsName.Parse(stored)));
    }

    /// <summary>
    ///     This test checks that only ASCII letters fold: a byte above 0x7F that differs by 0x20
    ///     stays different.
    /// </summary>
    [Fact]
    public void Equals_NonAsciiBytesThatDifferByCase_AreDifferent()
    {
        DnsName upper = DnsName.Parse(@"\193.example");
        DnsName lower = DnsName.Parse(@"\225.example");

        Assert.False(DnsNameComparer.Instance.Equals(new DnsNameSuffix(upper, 0), lower));
        Assert.False(DnsNameComparer.Instance.Equals(upper, lower));
    }

    /// <summary>
    ///     This test checks that probing a populated dictionary allocates no bytes.
    /// </summary>
    [Fact]
    public void SuffixLookup_ProbingAName_AllocatesNothing()
    {
        Dictionary<DnsName, int> zones = new(DnsNameComparer.Instance)
        {
            [DnsName.Parse("example.com")] = 1,
            [DnsName.Root] = 2,
        };
        DnsName name = DnsName.Parse("www.sub.Example.com");
        Dictionary<DnsName, int>.AlternateLookup<DnsNameSuffix> lookup =
            zones.GetAlternateLookup<DnsNameSuffix>();

        // Warm up so JIT work and the one-time build of the hash code are not counted.
        Probe(lookup, name);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            Probe(lookup, name);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    /// <summary>
    ///     This method walks a name up to the root through the lookup, as the zone lookups do.
    /// </summary>
    private static int Probe(
        Dictionary<DnsName, int>.AlternateLookup<DnsNameSuffix> lookup,
        DnsName name
    )
    {
        for (int skip = 0; skip <= name.Labels.Count; skip++)
            if (lookup.TryGetValue(new DnsNameSuffix(name, skip), out int value))
                return value;

        return 0;
    }

    /// <summary>
    ///     This method builds a random name of the given label count from the alphabet.
    /// </summary>
    private static string RandomName(Random random, string[] alphabet, int labels)
    {
        List<string> parts = [];

        for (int i = 0; i < labels; i++)
            parts.Add(alphabet[random.Next(alphabet.Length)]);

        return string.Join('.', parts);
    }

    /// <summary>
    ///     This method builds a name of four labels (253 octets on the wire with the root label,
    ///     the maximum is 255). Variant 1 is upper case and variant 2 drops the first label.
    /// </summary>
    private static string LongName(int variant)
    {
        string label = new('a', 62);
        string[] labels = [label, label, label, new('b', 61)];

        if (variant == 1)
            labels = [.. labels.Select(l => l.ToUpperInvariant())];

        if (variant == 2)
            labels = [.. labels.Skip(1)];

        return string.Join('.', labels);
    }
}

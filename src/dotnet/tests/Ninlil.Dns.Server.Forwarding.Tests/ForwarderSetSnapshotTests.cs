using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="ForwarderSetSnapshot.Find" /> on in-memory snapshots: the
///     allocation-free path (a dictionary built with <see cref="DnsNameComparer" />) must answer
///     exactly like the path for a hand-built dictionary, which builds a name per suffix and is
///     the reference here.
/// </summary>
public class ForwarderSetSnapshotTests
{
    private static readonly string[] ZoneTexts =
    [
        ".",
        "example.com",
        "badexample.com",
        "Corp.Example.ORG",
        "sub.example.com",
        @"x\.y.net",
        "deep.a.b.c.d.test",
    ];

    private static readonly string[] QueryTexts =
    [
        ".",
        "com",
        "example.com",
        "EXAMPLE.COM",
        "www.example.com",
        "www.badexample.com",
        "ample.com",
        "a.sub.EXAMPLE.com",
        "corp.example.org",
        "x.CORP.example.org",
        @"w.x\.y.net",
        "w.x.y.net",
        "q.deep.a.b.c.d.test",
        "a.b.c.d.test",
        "other.invalid",
    ];

    /// <summary>
    ///     This test checks that both snapshots return the same set for every query, with and
    ///     without a root zone.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Find_ComparerDictionaryAndPlainDictionary_ReturnTheSameSet(bool withRootZone)
    {
        Dictionary<DnsName, ForwarderSet> plain = [];
        Dictionary<DnsName, ForwarderSet> fast = new(DnsNameComparer.Instance);

        for (int i = 0; i < ZoneTexts.Length; i++)
        {
            if (!withRootZone && ZoneTexts[i] == ".")
                continue;

            ForwarderSet set = new([$"192.0.2.{i + 1}"], true);
            plain.TryAdd(DnsName.Parse(ZoneTexts[i]), set);
            fast.TryAdd(DnsName.Parse(ZoneTexts[i]), set);
        }

        ForwarderSet global = new(["192.0.2.200"], true);
        ForwarderSetSnapshot reference = new(plain, global);
        ForwarderSetSnapshot snapshot = new(fast, global);

        foreach (string text in QueryTexts)
        {
            DnsName name = DnsName.Parse(text);

            Assert.Same(reference.Find(name), snapshot.Find(name));
        }

        Assert.Equal(
            withRootZone ? "192.0.2.1" : "192.0.2.200",
            snapshot.Find(DnsName.Parse("other.invalid")).Definitions[0]
        );
        Assert.Equal("192.0.2.2", snapshot.Find(DnsName.Parse("WWW.Example.Com")).Definitions[0]);
        Assert.Equal(
            withRootZone ? "192.0.2.1" : "192.0.2.200",
            snapshot.Find(DnsName.Parse("www.badexample.net")).Definitions[0]
        );
    }

    /// <summary>
    ///     This test checks that a lookup in a snapshot built with the comparer allocates no
    ///     bytes, for a hit, a miss that falls to the global set, and the root name.
    /// </summary>
    [Fact]
    public void Find_InAComparerSnapshot_AllocatesNothing()
    {
        Dictionary<DnsName, ForwarderSet> zones = new(DnsNameComparer.Instance)
        {
            [DnsName.Parse("corp.example")] = new ForwarderSet(["192.0.2.1"], true),
        };
        ForwarderSetSnapshot snapshot = new(zones, new ForwarderSet(["192.0.2.200"], true));
        DnsName hit = DnsName.Parse("www.sub.corp.example");
        DnsName miss = DnsName.Parse("www.example.org");

        // Warm up so JIT work and the one-time build of each name's hash code are not counted.
        snapshot.Find(hit);
        snapshot.Find(miss);
        snapshot.Find(DnsName.Root);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            snapshot.Find(hit);
            snapshot.Find(miss);
            snapshot.Find(DnsName.Root);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

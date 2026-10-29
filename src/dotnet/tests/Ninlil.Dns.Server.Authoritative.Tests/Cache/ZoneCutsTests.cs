using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Cache;

/// <summary>
///     This class tests <see cref="ZoneCuts" />, the pure helper that keeps only the topmost
///     delegation points of a zone and tells whether a name lies below one. It walks the ancestors
///     of each name with <see cref="DnsName.Suffix" />, so the cases include capitals and names
///     several labels below a cut.
/// </summary>
public sealed class ZoneCutsTests
{
    private static readonly DnsName Apex = DnsName.Parse("example.com");

    [Fact]
    public void FindTopmostCuts_WithNestedAndSeparateCuts_KeepsOnlyTheTopmost()
    {
        HashSet<DnsName> cuts = ZoneCuts.FindTopmostCuts(
            Apex,
            [
                DnsName.Parse("example.com"),
                DnsName.Parse("sub.example.com"),
                DnsName.Parse("deep.SUB.example.com"),
                DnsName.Parse("other.example.com"),
            ]
        );

        Assert.Equal(2, cuts.Count);
        Assert.Contains(DnsName.Parse("sub.example.com"), cuts);
        Assert.Contains(DnsName.Parse("other.example.com"), cuts);
    }

    [Theory]
    [InlineData("a.b.sub.example.com", true)]
    [InlineData("A.SUB.example.com", true)]
    [InlineData("sub.example.com", false)]
    [InlineData("www.example.com", false)]
    [InlineData("example.com", false)]
    public void IsBelowACut_ForANameAtOrBelowACut_IsTrueOnlyStrictlyBelow(
        string name,
        bool expected
    )
    {
        HashSet<DnsName> cuts = [DnsName.Parse("sub.example.com")];

        Assert.Equal(expected, ZoneCuts.IsBelowACut(DnsName.Parse(name), Apex, cuts));
    }
}

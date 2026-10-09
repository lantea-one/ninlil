using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="InsecureDomainPolicy" /> and the built-in list of
///     <see cref="RecursiveServerOptions" /> with plain options objects: which names match, how
///     the custom list replaces the built-in one, and when the fingerprint changes.
/// </summary>
public sealed class InsecureDomainPolicyTests
{
    private static InsecureDomainPolicy CreatePolicy(RecursiveServerOptions options) =>
        new(new SwappableOptionsMonitor<RecursiveServerOptions>(options));

    private static bool IsInsecure(InsecureDomainPolicy policy, string name) =>
        policy.IsInsecure(DnsName.Parse(name));

    [Theory]
    [InlineData("home.arpa")]
    [InlineData("printer.home.arpa")]
    [InlineData("internal")]
    [InlineData("www.corp.INTERNAL")]
    [InlineData("5.1.0.10.in-addr.arpa")]
    [InlineData("1.16.172.in-addr.arpa")]
    [InlineData("1.31.172.in-addr.arpa")]
    [InlineData("1.1.168.192.in-addr.arpa")]
    [InlineData("1.0.0.127.in-addr.arpa")]
    [InlineData("1.0.254.169.in-addr.arpa")]
    [InlineData("1.64.100.in-addr.arpa")]
    [InlineData("1.127.100.in-addr.arpa")]
    [InlineData("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.ip6.arpa")]
    [InlineData("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.d.f.ip6.arpa")]
    [InlineData("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.8.e.f.ip6.arpa")]
    public void IsInsecure_ForANameUnderTheBuiltInList_IsTrue(string name)
    {
        InsecureDomainPolicy policy = CreatePolicy(new RecursiveServerOptions());

        Assert.True(IsInsecure(policy, name));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("arpa")]
    [InlineData("in-addr.arpa")]
    [InlineData("1.15.172.in-addr.arpa")]
    [InlineData("1.32.172.in-addr.arpa")]
    [InlineData("1.63.100.in-addr.arpa")]
    [InlineData("1.128.100.in-addr.arpa")]
    [InlineData("notinternal")]
    [InlineData("internal.example.com")]
    [InlineData("home.arpa.example.com")]
    public void IsInsecure_ForANameOutsideTheBuiltInList_IsFalse(string name)
    {
        InsecureDomainPolicy policy = CreatePolicy(new RecursiveServerOptions());

        Assert.False(IsInsecure(policy, name));
    }

    [Fact]
    public void BuiltInInsecureDomains_HaveNoDuplicatesAndAllParse()
    {
        IReadOnlyList<string> domains = RecursiveServerOptions.BuiltInInsecureDomains;

        Assert.Equal(domains.Count, domains.Select(d => d.ToLowerInvariant()).Distinct().Count());
        Assert.All(domains, domain => Assert.True(DnsName.TryParse(domain, out _), domain));
    }

    [Fact]
    public void IsInsecure_WithACustomList_UsesOnlyThatList()
    {
        InsecureDomainPolicy policy = CreatePolicy(
            new RecursiveServerOptions
            {
                UseCustomInsecureDomains = true,
                InsecureDomains = ["corp.example"],
            }
        );

        Assert.True(IsInsecure(policy, "host.corp.example"));
        Assert.False(IsInsecure(policy, "www.corp.internal"));
    }

    [Fact]
    public void IsInsecure_WithAnEmptyCustomList_ExemptsNothing()
    {
        InsecureDomainPolicy policy = CreatePolicy(
            new RecursiveServerOptions { UseCustomInsecureDomains = true }
        );

        Assert.False(IsInsecure(policy, "www.corp.internal"));
    }

    [Fact]
    public void IsInsecure_WithACustomListHoldingOnlyTheDisabledList_IgnoresIt()
    {
        InsecureDomainPolicy policy = CreatePolicy(
            new RecursiveServerOptions { InsecureDomains = ["corp.example"] }
        );

        Assert.False(IsInsecure(policy, "host.corp.example"));
        Assert.True(IsInsecure(policy, "www.corp.internal"));
    }

    [Fact]
    public void IsInsecure_ForARootEntryOrAnInvalidEntry_IgnoresTheEntry()
    {
        InsecureDomainPolicy policy = CreatePolicy(
            new RecursiveServerOptions
            {
                UseCustomInsecureDomains = true,
                InsecureDomains = [".", "", "a..b", "ok.example"],
            }
        );

        Assert.False(IsInsecure(policy, "www.example.com"));
        Assert.True(IsInsecure(policy, "www.ok.example"));
    }

    [Fact]
    public void Fingerprint_ChangesOnlyWhenTheSetOfNamesChanges()
    {
        SwappableOptionsMonitor<RecursiveServerOptions> monitor = new(
            new RecursiveServerOptions
            {
                UseCustomInsecureDomains = true,
                InsecureDomains = ["a.example", "B.example"],
            }
        );
        InsecureDomainPolicy policy = new(monitor);
        string first = policy.Fingerprint;

        monitor.CurrentValue = new RecursiveServerOptions
        {
            UseCustomInsecureDomains = true,
            InsecureDomains = ["b.example", "A.EXAMPLE"],
        };
        string sameSet = policy.Fingerprint;

        monitor.CurrentValue = new RecursiveServerOptions
        {
            UseCustomInsecureDomains = true,
            InsecureDomains = ["a.example"],
        };

        Assert.Equal(first, sameSet);
        Assert.NotEqual(first, policy.Fingerprint);
    }
}

using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class proves <see cref="RaAdvertisementProvider.BuildCurrentAdvertisement" />
///     builds a correct <see cref="RouterAdvertisementMessage" /> straight from
///     <see cref="RaOptions" /> -- the one real decision this engine makes, since there is no
///     per-client state involved at all (unlike the DHCPv4/DHCPv6 engines' own request-handler
///     tests, this needs no database, scope, or lease fixture whatsoever).
/// </summary>
public class RaAdvertisementProviderTests
{
    [Fact]
    public void BuildCurrentAdvertisement_WithNothingConfiguredAtAll_ReturnsNull()
    {
        RaAdvertisementProvider provider = NewProvider(new RaOptions());

        Assert.Null(provider.BuildCurrentAdvertisement());
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithOnlyRouterLifetimeSet_StillAdvertises()
    {
        RaAdvertisementProvider provider = NewProvider(
            new RaOptions { RouterLifetime = TimeSpan.FromSeconds(1800) }
        );

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.Equal(TimeSpan.FromSeconds(1800), advertisement.RouterLifetime);
    }

    [Fact]
    public void BuildCurrentAdvertisement_CarriesEveryFixedFieldFromOptions()
    {
        RaOptions options = new()
        {
            ManagedAddressConfiguration = true,
            OtherConfiguration = true,
            RouterLifetime = TimeSpan.FromSeconds(900),
            CurrentHopLimit = 32,
            ReachableTime = TimeSpan.FromSeconds(45),
            RetransTimer = TimeSpan.FromSeconds(2),
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(advertisement.ManagedAddressConfiguration);
        Assert.True(advertisement.OtherConfiguration);
        Assert.Equal(TimeSpan.FromSeconds(900), advertisement.RouterLifetime);
        Assert.Equal(32, advertisement.CurrentHopLimit);
        Assert.Equal(TimeSpan.FromSeconds(45), advertisement.ReachableTime);
        Assert.Equal(TimeSpan.FromSeconds(2), advertisement.RetransTimer);
    }

    [Fact]
    public void BuildCurrentAdvertisement_TranslatesEveryConfiguredPrefixIntoAPrefixInformationOption()
    {
        RaOptions options = new()
        {
            Prefixes =
            [
                new RaAdvertisedPrefixOptions
                {
                    Prefix = "2001:db8:1::",
                    PrefixLength = 64,
                    OnLink = true,
                    Autonomous = true,
                    ValidLifetime = TimeSpan.FromDays(30),
                    PreferredLifetime = TimeSpan.FromDays(7),
                },
                new RaAdvertisedPrefixOptions
                {
                    Prefix = "2001:db8:2000::",
                    PrefixLength = 60,
                    OnLink = false,
                    Autonomous = false,
                },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(
            advertisement.TryGetPrefixInformationOptions(
                out IReadOnlyList<RaPrefixInformationOption> prefixes
            )
        );
        Assert.Equal(2, prefixes.Count);

        Assert.Equal(IPAddress.Parse("2001:db8:1::"), prefixes[0].Prefix);
        Assert.Equal(64, prefixes[0].PrefixLength);
        Assert.True(prefixes[0].OnLink);
        Assert.True(prefixes[0].Autonomous);
        Assert.Equal(TimeSpan.FromDays(30), prefixes[0].ValidLifetime);
        Assert.Equal(TimeSpan.FromDays(7), prefixes[0].PreferredLifetime);

        Assert.Equal(IPAddress.Parse("2001:db8:2000::"), prefixes[1].Prefix);
        Assert.Equal(60, prefixes[1].PrefixLength);
        Assert.False(prefixes[1].OnLink);
        Assert.False(prefixes[1].Autonomous);
    }

    [Fact]
    public void BuildCurrentAdvertisement_SkipsAnUnparseablePrefix_WithoutDroppingTheOthers()
    {
        RaOptions options = new()
        {
            Prefixes =
            [
                new RaAdvertisedPrefixOptions { Prefix = "not-a-real-address", PrefixLength = 64 },
                new RaAdvertisedPrefixOptions { Prefix = "2001:db8:1::", PrefixLength = 64 },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        advertisement.TryGetPrefixInformationOptions(
            out IReadOnlyList<RaPrefixInformationOption> prefixes
        );
        Assert.Equal(IPAddress.Parse("2001:db8:1::"), Assert.Single(prefixes).Prefix);
    }

    [Fact]
    public void BuildCurrentAdvertisement_SetsMtuOnlyWhenConfigured()
    {
        RaAdvertisementProvider provider = NewProvider(new RaOptions { LinkMtu = 1500 });

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(advertisement.TryGetMtu(out uint mtu));
        Assert.Equal(1500u, mtu);
    }

    [Fact]
    public void BuildCurrentAdvertisement_SetsRecursiveDnsServersWithTheConfiguredLifetime()
    {
        RaOptions options = new()
        {
            RecursiveDnsServers = ["2001:db8:1::1", "2001:db8:1::2"],
            RecursiveDnsServerLifetime = TimeSpan.FromSeconds(1200),
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(
            advertisement.TryGetRecursiveDnsServers(
                out TimeSpan lifetime,
                out IReadOnlyList<IPAddress> dnsServers
            )
        );
        Assert.Equal(TimeSpan.FromSeconds(1200), lifetime);
        Assert.Equal(2, dnsServers.Count);
    }

    [Fact]
    public void BuildCurrentAdvertisement_SetsDnsSearchListWithTheConfiguredLifetime()
    {
        RaOptions options = new()
        {
            DnsSearchListDomains = ["homelab.internal", "example.com"],
            DnsSearchListLifetime = TimeSpan.FromSeconds(900),
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(
            advertisement.TryGetDnsSearchList(
                out TimeSpan lifetime,
                out IReadOnlyList<string> domainNames
            )
        );
        Assert.Equal(TimeSpan.FromSeconds(900), lifetime);
        Assert.Equal(["homelab.internal", "example.com"], domainNames);
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithOnlyDnsSearchListConfigured_StillAdvertises()
    {
        // The DNS Search List alone -- with no prefix, router lifetime, or anything else set --
        // must be enough to make this engine actually build and send an advertisement, exactly
        // like the RDNSS-only and prefix-only cases already proven above.
        RaAdvertisementProvider provider = NewProvider(
            new RaOptions { DnsSearchListDomains = ["homelab.internal"] }
        );

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(advertisement.TryGetDnsSearchList(out _, out _));
    }

    [Fact]
    public void BuildCurrentAdvertisement_SkipsAnUnusableDnsSearchListDomain_WithoutDroppingTheOthers()
    {
        RaOptions options = new()
        {
            DnsSearchListDomains = ["has..empty.label", "homelab.internal"],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(
            advertisement.TryGetDnsSearchList(out _, out IReadOnlyList<string> domainNames)
        );
        Assert.Equal(["homelab.internal"], domainNames);
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithEveryDnsSearchListDomainUnusable_OmitsTheOptionEntirely()
    {
        RaOptions options = new()
        {
            RouterLifetime = TimeSpan.FromSeconds(1800),
            DnsSearchListDomains = ["has..empty.label"],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.False(advertisement.TryGetDnsSearchList(out _, out _));
    }

    [Fact]
    public void BuildCurrentAdvertisement_TranslatesEveryConfiguredRouteIntoARouteInformationOption()
    {
        RaOptions options = new()
        {
            Routes =
            [
                new RaAdvertisedRouteOptions
                {
                    Prefix = "2001:db8:5::",
                    PrefixLength = 64,
                    Preference = RaRoutePreferenceEnum.High,
                    RouteLifetime = TimeSpan.FromSeconds(900),
                },
                new RaAdvertisedRouteOptions
                {
                    PrefixLength = 0,
                    Preference = RaRoutePreferenceEnum.Low,
                    RouteLifetime = TimeSpan.FromSeconds(300),
                },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(
            advertisement.TryGetRouteInformationOptions(
                out IReadOnlyList<RaRouteInformationOption> routes
            )
        );
        Assert.Equal(2, routes.Count);

        Assert.Equal(IPAddress.Parse("2001:db8:5::"), routes[0].Prefix);
        Assert.Equal(64, routes[0].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.High, routes[0].Preference);
        Assert.Equal(TimeSpan.FromSeconds(900), routes[0].RouteLifetime);

        Assert.Equal(0, routes[1].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.Low, routes[1].Preference);
        Assert.Equal(TimeSpan.FromSeconds(300), routes[1].RouteLifetime);
    }

    [Fact]
    public void BuildCurrentAdvertisement_SkipsAnUnparseableRoute_WithoutDroppingTheOthers()
    {
        RaOptions options = new()
        {
            Routes =
            [
                new RaAdvertisedRouteOptions { Prefix = "not-a-real-address", PrefixLength = 64 },
                new RaAdvertisedRouteOptions { Prefix = "2001:db8:5::", PrefixLength = 64 },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        advertisement.TryGetRouteInformationOptions(
            out IReadOnlyList<RaRouteInformationOption> routes
        );
        Assert.Equal(IPAddress.Parse("2001:db8:5::"), Assert.Single(routes).Prefix);
    }

    [Fact]
    public void BuildCurrentAdvertisement_SkipsARouteWithAnOutOfRangePrefixLength_WithoutDroppingTheOthers()
    {
        RaOptions options = new()
        {
            Routes =
            [
                new RaAdvertisedRouteOptions { Prefix = "2001:db8:5::", PrefixLength = 200 },
                new RaAdvertisedRouteOptions { Prefix = "2001:db8:6::", PrefixLength = 64 },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        advertisement.TryGetRouteInformationOptions(
            out IReadOnlyList<RaRouteInformationOption> routes
        );
        Assert.Equal(IPAddress.Parse("2001:db8:6::"), Assert.Single(routes).Prefix);
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithOnlyARouteConfigured_StillAdvertises()
    {
        // A route alone -- with no prefix, router lifetime, or anything else set -- must be
        // enough to make this engine actually build and send an advertisement, exactly like the
        // RDNSS-only/prefix-only/DNS-Search-List-only cases already proven above.
        RaAdvertisementProvider provider = NewProvider(
            new RaOptions
            {
                Routes =
                [
                    new RaAdvertisedRouteOptions { Prefix = "2001:db8:5::", PrefixLength = 64 },
                ],
            }
        );

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement();

        Assert.NotNull(advertisement);
        Assert.True(advertisement.TryGetRouteInformationOptions(out _));
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithNoInterfaceOverridesConfigured_IsIdenticalAcrossEveryInterfaceName()
    {
        // Verification (a): with no interface overrides configured at all, behavior is unchanged
        // -- every interface (named or not) gets the exact same global content, byte-for-byte.
        RaOptions options = new()
        {
            RouterLifetime = TimeSpan.FromSeconds(1800),
            CurrentHopLimit = 32,
            Prefixes =
            [
                new RaAdvertisedPrefixOptions { Prefix = "2001:db8:1::", PrefixLength = 64 },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage noInterfaceKnown = provider.BuildCurrentAdvertisement();
        RouterAdvertisementMessage eth0 = provider.BuildCurrentAdvertisement("eth0");
        RouterAdvertisementMessage eth1 = provider.BuildCurrentAdvertisement("eth1");

        foreach (RouterAdvertisementMessage advertisement in new[] { noInterfaceKnown, eth0, eth1 })
        {
            Assert.NotNull(advertisement);
            Assert.Equal(TimeSpan.FromSeconds(1800), advertisement.RouterLifetime);
            Assert.Equal(32, advertisement.CurrentHopLimit);
            Assert.True(advertisement.TryGetPrefixInformationOptions(out var prefixes));
            Assert.Equal(IPAddress.Parse("2001:db8:1::"), Assert.Single(prefixes).Prefix);
        }
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithAMatchingInterfaceOverride_UsesTheOverriddenFieldsForThatInterfaceOnly()
    {
        // Verification (b): an interface with an override gets its own distinct content, while an
        // interface with no matching override still falls back to the global default.
        RaOptions options = new()
        {
            RouterLifetime = TimeSpan.FromSeconds(1800),
            CurrentHopLimit = 64,
            RecursiveDnsServers = ["2001:db8:1::1"],
            InterfaceOverrides =
            [
                new RaInterfaceOverrideOptions
                {
                    InterfaceName = "eth0",
                    RouterLifetime = TimeSpan.FromSeconds(300),
                    CurrentHopLimit = 10,
                },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage overridden = provider.BuildCurrentAdvertisement("eth0");
        RouterAdvertisementMessage fallsBackToGlobal = provider.BuildCurrentAdvertisement("eth1");

        Assert.NotNull(overridden);
        Assert.Equal(TimeSpan.FromSeconds(300), overridden.RouterLifetime);
        Assert.Equal(10, overridden.CurrentHopLimit);
        // A field the override never touches still inherits the global default.
        Assert.True(overridden.TryGetRecursiveDnsServers(out _, out var overriddenDnsServers));
        Assert.Single(overriddenDnsServers);

        Assert.NotNull(fallsBackToGlobal);
        Assert.Equal(TimeSpan.FromSeconds(1800), fallsBackToGlobal.RouterLifetime);
        Assert.Equal(64, fallsBackToGlobal.CurrentHopLimit);
    }

    [Fact]
    public void BuildCurrentAdvertisement_MatchesAnInterfaceOverrideNameCaseInsensitively()
    {
        RaOptions options = new()
        {
            InterfaceOverrides =
            [
                new RaInterfaceOverrideOptions
                {
                    InterfaceName = "Eth0",
                    RouterLifetime = TimeSpan.FromSeconds(900),
                },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement("eth0");

        Assert.NotNull(advertisement);
        Assert.Equal(TimeSpan.FromSeconds(900), advertisement.RouterLifetime);
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithANonNullOverrideList_ReplacesTheGlobalListWholesaleEvenWhenEmpty()
    {
        // A non-null (but empty) override list is a deliberate "advertise nothing here" wholesale
        // replacement, distinct from leaving the field null (which inherits the global list).
        RaOptions options = new()
        {
            RecursiveDnsServers = ["2001:db8:1::1"],
            InterfaceOverrides =
            [
                new RaInterfaceOverrideOptions
                {
                    InterfaceName = "eth0",
                    RouterLifetime = TimeSpan.FromSeconds(1800),
                    RecursiveDnsServers = [],
                },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        RouterAdvertisementMessage advertisement = provider.BuildCurrentAdvertisement("eth0");

        Assert.NotNull(advertisement);
        Assert.False(advertisement.TryGetRecursiveDnsServers(out _, out _));
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithAnExcludedInterface_ReturnsNull()
    {
        // Verification (c): an interface explicitly excluded gets no advertisement at all, even
        // though the global default has plenty to advertise.
        RaOptions options = new()
        {
            RouterLifetime = TimeSpan.FromSeconds(1800),
            Prefixes =
            [
                new RaAdvertisedPrefixOptions { Prefix = "2001:db8:1::", PrefixLength = 64 },
            ],
            InterfaceOverrides =
            [
                new RaInterfaceOverrideOptions { InterfaceName = "eth0", Exclude = true },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        Assert.Null(provider.BuildCurrentAdvertisement("eth0"));
        // A different, non-excluded interface is unaffected.
        Assert.NotNull(provider.BuildCurrentAdvertisement("eth1"));
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithAnExcludedInterfaceAndNoInterfaceNameGiven_StillAdvertisesTheGlobalDefault()
    {
        // A caller that does not know which interface a send is for (interfaceName: null) can
        // never honor a per-interface exclusion, since there is no name to match against -- see
        // IRaAdvertisementProvider's own doc comment.
        RaOptions options = new()
        {
            RouterLifetime = TimeSpan.FromSeconds(1800),
            InterfaceOverrides =
            [
                new RaInterfaceOverrideOptions { InterfaceName = "eth0", Exclude = true },
            ],
        };
        RaAdvertisementProvider provider = NewProvider(options);

        Assert.NotNull(provider.BuildCurrentAdvertisement());
    }

    /// <summary>
    ///     This method builds a real <see cref="RaAdvertisementProvider" /> against a fixed,
    ///     never-changing options snapshot -- this engine has no need to observe a live
    ///     configuration reload mid-test.
    /// </summary>
    private static RaAdvertisementProvider NewProvider(RaOptions options) =>
        new(new FixedOptionsMonitor(options));

    /// <summary>
    ///     This class is a minimal <see cref="IOptionsMonitor{TOptions}" /> stand-in that always
    ///     reports the same fixed value -- this project has no dependency on a real
    ///     dependency-injection host/configuration builder at all, so a hand-rolled fake is
    ///     simpler than assembling one just to get a real <see cref="IOptionsMonitor{TOptions}" />.
    /// </summary>
    private sealed class FixedOptionsMonitor(RaOptions value) : IOptionsMonitor<RaOptions>
    {
        public RaOptions CurrentValue { get; } = value;

        public RaOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<RaOptions, string> listener) => null;
    }
}

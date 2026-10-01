using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks that <see cref="RaAdvertisementProvider.BuildCurrentAdvertisement" />
///     builds the right <see cref="RouterAdvertisementMessage" /> from <see cref="RaOptions" />:
///     flags and timers, prefixes, MTU, RDNSS, DNSSL, routes, skipped bad entries, the "nothing to
///     advertise" case, and per-interface overrides and exclusions. It needs no database, only a
///     fixed options monitor.
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
        // A DNS Search List alone is enough for an advertisement, like RDNSS or a prefix alone.
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
        // A route alone is enough for an advertisement, like the cases above.
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
        // (a) With no overrides, every interface, named or not, gets the same global content.
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
        // (b) An interface with an override gets its own content; one without falls back to the
        // global values.
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
        // A field the override leaves unset inherits the global value.
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
        // An empty (not null) override list replaces the global list with nothing; null would
        // inherit it.
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
        // (c) An excluded interface gets no advertisement, although the global content has plenty.
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
        // Another interface is unaffected.
        Assert.NotNull(provider.BuildCurrentAdvertisement("eth1"));
    }

    [Fact]
    public void BuildCurrentAdvertisement_WithAnExcludedInterfaceAndNoInterfaceNameGiven_StillAdvertisesTheGlobalDefault()
    {
        // With no interface name (null) there is nothing to match, so the exclusion cannot apply
        // (see IRaAdvertisementProvider).
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
    ///     This method builds a <see cref="RaAdvertisementProvider" /> on fixed options.
    /// </summary>
    private static RaAdvertisementProvider NewProvider(RaOptions options) =>
        new(new FixedOptionsMonitor(options));

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> fake that always returns the
    ///     same value, simpler than building a host and configuration for these tests.
    /// </summary>
    private sealed class FixedOptionsMonitor(RaOptions value) : IOptionsMonitor<RaOptions>
    {
        public RaOptions CurrentValue { get; } = value;

        public RaOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<RaOptions, string> listener) => null;
    }
}

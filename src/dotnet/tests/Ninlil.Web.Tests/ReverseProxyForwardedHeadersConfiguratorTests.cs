using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Web.Middleware;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ReverseProxyForwardedHeadersConfigurator" /> directly -- the
///     dedicated regression test for a real bug found while verifying reverse-proxy configuration
///     end to end (see that class's own doc comment for the full story): a brand-new
///     <see cref="ForwardedHeadersOptions" /> instance already pre-trusts loopback on its own,
///     and this configurator must clear those defaults before adding this project's own
///     configured entries, not merely append to them.
/// </summary>
public class ReverseProxyForwardedHeadersConfiguratorTests
{
    [Fact]
    public void Configure_WithNothingConfigured_LeavesBothCollectionsCompletelyEmpty()
    {
        ForwardedHeadersOptions forwardedHeadersOptions = new();

        // Sanity-check the real framework default this test exists to defeat -- if this
        // assertion itself ever starts failing, ForwardedHeadersOptions's own default
        // constructor behavior changed, and this whole class's reasoning needs re-checking.
        Assert.NotEmpty(forwardedHeadersOptions.KnownProxies);
        Assert.NotEmpty(forwardedHeadersOptions.KnownIPNetworks);

        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            new ReverseProxyOptions(),
            NullLogger.Instance
        );

        Assert.Empty(forwardedHeadersOptions.KnownProxies);
        Assert.Empty(forwardedHeadersOptions.KnownIPNetworks);
    }

    [Fact]
    public void Configure_WithEntriesConfigured_ContainsOnlyThoseEntriesNotTheFrameworkDefaults()
    {
        ForwardedHeadersOptions forwardedHeadersOptions = new();
        ReverseProxyOptions reverseProxyOptions = new()
        {
            KnownProxies = ["203.0.113.10"],
            KnownNetworks = ["10.0.0.0/24"],
        };

        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            reverseProxyOptions,
            NullLogger.Instance
        );

        Assert.Single(forwardedHeadersOptions.KnownProxies);
        Assert.Equal("203.0.113.10", forwardedHeadersOptions.KnownProxies[0].ToString());
        Assert.Single(forwardedHeadersOptions.KnownIPNetworks);
        Assert.DoesNotContain(
            forwardedHeadersOptions.KnownIPNetworks,
            network => network.ToString() == "127.0.0.0/8"
        );
    }

    [Fact]
    public void Configure_WithAMalformedEntry_SkipsItWithoutThrowing()
    {
        ForwardedHeadersOptions forwardedHeadersOptions = new();
        ReverseProxyOptions reverseProxyOptions = new()
        {
            KnownProxies = ["not-an-ip-address", "203.0.113.10"],
            KnownNetworks = ["not-a-cidr-network"],
        };

        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            reverseProxyOptions,
            NullLogger.Instance
        );

        Assert.Single(forwardedHeadersOptions.KnownProxies);
        Assert.Empty(forwardedHeadersOptions.KnownIPNetworks);
    }
}

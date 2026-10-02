using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Web.Middleware;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="ReverseProxyForwardedHeadersConfigurator" /> directly, without
///     a host. A new <see cref="ForwardedHeadersOptions" /> already trusts loopback, so the
///     configurator must clear those defaults before adding the configured entries rather than
///     append to them; otherwise forwarded headers from loopback would be trusted even when no
///     proxy is configured (see <see cref="ReverseProxyForwardedHeadersConfigurator" />).
/// </summary>
public class ReverseProxyForwardedHeadersConfiguratorTests
{
    [Fact]
    public void Configure_WithNothingConfigured_LeavesBothCollectionsCompletelyEmpty()
    {
        ForwardedHeadersOptions forwardedHeadersOptions = new();

        // Check the framework default this class guards against. If this assertion fails,
        // the ForwardedHeadersOptions constructor has changed and the reasoning needs checking.
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

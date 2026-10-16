using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="ForwarderEndpointCache" /> and the no-DNS parsing it relies
///     on. The cache runs on the real definition parser, a <see cref="FakeForwarderHostResolver" />
///     whose lookups the test completes by hand, and a <see cref="ManualTimeProvider" />, so the
///     refresh age is exact and no real DNS lookup or wall-clock wait happens.
/// </summary>
public sealed class ForwarderEndpointCacheTests
{
    private const string HostDefinition = "udp://fwd.example:5353";

    private readonly FakeForwarderHostResolver _resolver = new();
    private readonly ManualTimeProvider _time = new();
    private readonly CapturingLogger<ForwarderEndpointCache> _logger = new();
    private readonly ForwarderEndpointCache _cache;

    public ForwarderEndpointCacheTests()
    {
        _cache = new ForwarderEndpointCache(
            new UpstreamForwarderDefinitionParser(),
            _resolver,
            _logger,
            _time
        );
    }

    /// <summary>
    ///     This method starts the lookup of <see cref="HostDefinition" />, answers it and waits
    ///     until the cache has stored the result.
    /// </summary>
    private async Task ResolveFirstAsync(string address)
    {
        _cache.TryGetSendable(HostDefinition);
        _resolver.Complete(_resolver.Calls.Count - 1, IPAddress.Parse(address));
        await _cache.WhenIdleAsync();
    }

    [Fact]
    public void Parse_OfAHostNameDefinition_LeavesTheAddressNullAndKeepsTheName()
    {
        UpstreamForwarderDefinition definition = new UpstreamForwarderDefinitionParser().Parse(
            "tls://name.invalid"
        );

        Assert.Null(definition.Address);
        Assert.Equal("name.invalid", definition.TlsHostName);
        Assert.Equal(853, definition.Port);
    }

    [Fact]
    public void TryGetSendable_ForADohUrl_ReturnsItAtOnceAndNeverResolves()
    {
        UpstreamForwarderDefinition definition = _cache.TryGetSendable(
            "https://doh.example/dns-query"
        );

        Assert.NotNull(definition);
        Assert.Equal(UpstreamForwarderProtocolEnum.Https, definition.Protocol);
        Assert.Equal("doh.example", definition.DohUri.Host);
        Assert.Empty(_resolver.Calls);
    }

    [Fact]
    public void TryGetSendable_ForALiteralAddress_ReturnsItAtOnceAndNeverResolves()
    {
        UpstreamForwarderDefinition definition = _cache.TryGetSendable("192.0.2.7:5353");

        Assert.Equal(IPAddress.Parse("192.0.2.7"), definition.Address);
        Assert.Equal(5353, definition.Port);
        Assert.Empty(_resolver.Calls);
    }

    [Fact]
    public async Task TryGetSendable_ForANewHostName_ReturnsNullAndStartsOneLookup()
    {
        Assert.Null(_cache.TryGetSendable(HostDefinition));
        Assert.Null(_cache.TryGetSendable(HostDefinition));
        Assert.Equal(["fwd.example"], _resolver.Calls);

        _resolver.Complete(0, IPAddress.Parse("192.0.2.1"));
        await _cache.WhenIdleAsync();

        UpstreamForwarderDefinition definition = _cache.TryGetSendable(HostDefinition);
        Assert.Equal(IPAddress.Parse("192.0.2.1"), definition.Address);
        Assert.Equal(5353, definition.Port);
        Assert.Equal("fwd.example", definition.TlsHostName);
        Assert.Single(_resolver.Calls);
    }

    [Fact]
    public async Task TryGetSendable_ForAHostWithBothFamilies_PicksTheIPv4Address()
    {
        _cache.TryGetSendable(HostDefinition);
        _resolver.Complete(0, IPAddress.Parse("2001:db8::1"), IPAddress.Parse("192.0.2.9"));
        await _cache.WhenIdleAsync();

        Assert.Equal(IPAddress.Parse("192.0.2.9"), _cache.TryGetSendable(HostDefinition).Address);
    }

    [Fact]
    public async Task TryGetSendable_ForAHostWithOnlyIPv6_UsesIt()
    {
        _cache.TryGetSendable(HostDefinition);
        _resolver.Complete(0, IPAddress.Parse("2001:db8::1"));
        await _cache.WhenIdleAsync();

        Assert.Equal(IPAddress.Parse("2001:db8::1"), _cache.TryGetSendable(HostDefinition).Address);
    }

    [Fact]
    public async Task TryGetSendable_BeforeTheRefreshIntervalPasses_StartsNoNewLookup()
    {
        await ResolveFirstAsync("192.0.2.1");

        _time.Advance(ForwarderEndpointCache.RefreshInterval - TimeSpan.FromSeconds(1));
        _cache.TryGetSendable(HostDefinition);

        Assert.Single(_resolver.Calls);
    }

    [Fact]
    public async Task TryGetSendable_AfterTheRefreshInterval_ServesTheOldAddressWhileOneRefreshRuns()
    {
        await ResolveFirstAsync("192.0.2.1");
        _time.Advance(ForwarderEndpointCache.RefreshInterval);

        UpstreamForwarderDefinition during = _cache.TryGetSendable(HostDefinition);
        UpstreamForwarderDefinition duringAgain = _cache.TryGetSendable(HostDefinition);

        Assert.Equal(IPAddress.Parse("192.0.2.1"), during.Address);
        Assert.Equal(IPAddress.Parse("192.0.2.1"), duringAgain.Address);
        Assert.Equal(2, _resolver.Calls.Count);

        _resolver.Complete(1, IPAddress.Parse("192.0.2.2"));
        await _cache.WhenIdleAsync();

        Assert.Equal(IPAddress.Parse("192.0.2.2"), _cache.TryGetSendable(HostDefinition).Address);
        Assert.Equal(2, _resolver.Calls.Count);
    }

    [Fact]
    public async Task TryGetSendable_WhenRefreshesFail_KeepsTheLastGoodAddressAndWarnsOncePerStreak()
    {
        await ResolveFirstAsync("192.0.2.1");

        // Failure 1: still quiet.
        _time.Advance(ForwarderEndpointCache.RefreshInterval);
        _cache.TryGetSendable(HostDefinition);
        _resolver.Fail(1);
        await _cache.WhenIdleAsync();
        Assert.Equal(0, _logger.Count(LogLevel.Warning));

        // Failure 2 ends the streak of two: one Warning.
        _time.Advance(ForwarderEndpointCache.RefreshInterval);
        _cache.TryGetSendable(HostDefinition);
        _resolver.Fail(2);
        await _cache.WhenIdleAsync();
        Assert.Equal(1, _logger.Count(LogLevel.Warning));

        // Failure 3 is in the same streak: no second Warning.
        _time.Advance(ForwarderEndpointCache.RefreshInterval);
        _cache.TryGetSendable(HostDefinition);
        _resolver.Fail(3);
        await _cache.WhenIdleAsync();
        Assert.Equal(1, _logger.Count(LogLevel.Warning));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), _cache.TryGetSendable(HostDefinition).Address);

        // The name resolves again: one Information, and the new address is used.
        _time.Advance(ForwarderEndpointCache.RefreshInterval);
        _cache.TryGetSendable(HostDefinition);
        _resolver.Complete(4, IPAddress.Parse("192.0.2.5"));
        await _cache.WhenIdleAsync();
        Assert.Equal(1, _logger.Count(LogLevel.Information));
        Assert.Equal(1, _logger.Count(LogLevel.Warning));
        Assert.Equal(IPAddress.Parse("192.0.2.5"), _cache.TryGetSendable(HostDefinition).Address);
    }

    [Fact]
    public async Task TryGetSendable_WhenTheFirstLookupFails_StaysUnsendableAndRetriesAfterTheInterval()
    {
        _cache.TryGetSendable(HostDefinition);
        _resolver.Fail(0);
        await _cache.WhenIdleAsync();

        Assert.Null(_cache.TryGetSendable(HostDefinition));
        Assert.Single(_resolver.Calls);

        _time.Advance(ForwarderEndpointCache.RefreshInterval);
        Assert.Null(_cache.TryGetSendable(HostDefinition));
        Assert.Equal(2, _resolver.Calls.Count);

        _resolver.Complete(1, IPAddress.Parse("192.0.2.3"));
        await _cache.WhenIdleAsync();
        Assert.Equal(IPAddress.Parse("192.0.2.3"), _cache.TryGetSendable(HostDefinition).Address);
    }

    [Fact]
    public async Task TryGetSendable_WhenALookupReturnsNoAddress_CountsAsAFailure()
    {
        _cache.TryGetSendable(HostDefinition);
        _resolver.Complete(0);
        await _cache.WhenIdleAsync();

        Assert.Null(_cache.TryGetSendable(HostDefinition));
    }

    [Fact]
    public async Task Retain_DropsADefinitionThatIsNoLongerConfigured()
    {
        await ResolveFirstAsync("192.0.2.1");

        _cache.Retain(new HashSet<string> { "192.0.2.50" });

        Assert.Null(_cache.TryGetSendable(HostDefinition));
        Assert.Equal(2, _resolver.Calls.Count);
    }

    [Fact]
    public async Task Retain_KeepsAConfiguredDefinition()
    {
        await ResolveFirstAsync("192.0.2.1");

        _cache.Retain(new HashSet<string> { HostDefinition });

        Assert.NotNull(_cache.TryGetSendable(HostDefinition));
        Assert.Single(_resolver.Calls);
    }

    [Fact]
    public void TryGetSendable_ForAnUnparseableDefinition_ReturnsNullAndWarnsOnce()
    {
        Assert.Null(_cache.TryGetSendable("tls://[2001:db8::1]x"));
        Assert.Null(_cache.TryGetSendable("tls://[2001:db8::1]x"));
        Assert.Null(_cache.TryGetSendable("tls://[2001:db8::1]x"));

        Assert.Equal(1, _logger.Count(LogLevel.Warning));
        Assert.Empty(_resolver.Calls);
    }
}

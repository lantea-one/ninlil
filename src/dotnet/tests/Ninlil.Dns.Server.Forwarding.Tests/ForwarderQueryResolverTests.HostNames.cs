using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests host-name forwarders: the
///     resolver never waits for a lookup, skips a forwarder whose address is unknown for the next
///     one, and uses the host's address once the background lookup has finished. The lookups run
///     through a <see cref="FakeForwarderHostResolver" /> the test completes by hand.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    [Fact]
    public async Task HostNames_WithTheLookupBlocked_AreSkippedForTheNextForwarderAtOnce()
    {
        using FakeUdpUpstream byName = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.77"))
        );
        using FakeUdpUpstream byAddress = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"udp://fwd.example:{byName.Port}", sortOrder: 0);
        await SeedForwarderAsync($"127.0.0.1:{byAddress.Port}", sortOrder: 1);

        FakeForwarderHostResolver hostResolver = new();
        CapturingLogger<ForwarderQueryResolver> resolverLog = new();
        CapturingLogger<ForwarderEndpointCache> cacheLog = new();
        ForwarderQueryResolver resolver = CreateResolver(
            endpointCache: new ForwarderEndpointCache(
                new UpstreamForwarderDefinitionParser(),
                hostResolver,
                cacheLog
            ),
            logger: resolverLog
        );

        for (ushort id = 1; id <= 5; id++)
        {
            DnsQueryResolution resolution = await resolver.TryResolveAsync(
                BuildContext(BuildQuery(id, $"q{id}.example.com."))
            );

            Assert.True(resolution.IsHandled);
        }

        // Every query was answered by the address forwarder; the host name was looked up once
        // and never became a Warning.
        Assert.Equal(5, byAddress.QueryCount);
        Assert.Equal(0, byName.QueryCount);
        Assert.Equal(["fwd.example"], hostResolver.Calls);
        Assert.Equal(0, resolverLog.Count(LogLevel.Warning));
        Assert.Equal(0, cacheLog.Count(LogLevel.Warning));
    }

    [Fact]
    public async Task HostNames_OnceTheLookupFinishes_AreUsedForLaterQueries()
    {
        using FakeUdpUpstream byName = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.77"))
        );
        await SeedForwarderAsync($"udp://fwd.example:{byName.Port}");

        FakeForwarderHostResolver hostResolver = new();
        ForwarderEndpointCache endpoints = new(
            new UpstreamForwarderDefinitionParser(),
            hostResolver,
            NullLogger<ForwarderEndpointCache>.Instance
        );
        ForwarderQueryResolver resolver = CreateResolver(endpointCache: endpoints);

        // The only forwarder has no address yet, so the first query falls through.
        DnsQueryResolution first = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(1, "a.example.com."))
        );
        Assert.False(first.IsHandled);

        hostResolver.Complete(0, IPAddress.Loopback);
        await endpoints.WhenIdleAsync();

        DnsQueryResolution second = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "b.example.com."))
        );
        Assert.True(second.IsHandled);
        Assert.Equal(1, byName.QueryCount);
    }

    [Fact]
    public async Task HostNames_OfARemovedForwarder_AreForgottenWhenTheSetChanges()
    {
        using FakeUdpUpstream byAddress = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync("udp://fwd.example:5353", sortOrder: 0);
        await SeedForwarderAsync($"127.0.0.1:{byAddress.Port}", sortOrder: 1);

        FakeForwarderHostResolver hostResolver = new();
        ForwarderEndpointCache endpoints = new(
            new UpstreamForwarderDefinitionParser(),
            hostResolver,
            NullLogger<ForwarderEndpointCache>.Instance
        );
        ForwarderQueryResolver resolver = CreateResolver(endpointCache: endpoints);

        await resolver.TryResolveAsync(BuildContext(BuildQuery(1, "a.example.com.")));
        hostResolver.Complete(0, IPAddress.Loopback);
        await endpoints.WhenIdleAsync();
        Assert.NotNull(endpoints.TryGetSendable("udp://fwd.example:5353"));

        await DisableGlobalForwardersAsync();
        await SeedForwarderAsync($"127.0.0.1:{byAddress.Port}", sortOrder: 1);
        await resolver.TryResolveAsync(BuildContext(BuildQuery(2, "b.example.com.")));

        // The host name is no longer configured, so its state is gone and a use looks it up anew.
        Assert.Null(endpoints.TryGetSendable("udp://fwd.example:5353"));
        Assert.Equal(2, hostResolver.Calls.Count);
    }
}

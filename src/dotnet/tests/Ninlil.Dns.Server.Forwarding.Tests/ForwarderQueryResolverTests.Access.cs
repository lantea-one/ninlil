using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests who
///     <see cref="ForwarderQueryResolver" /> forwards for: only a QUERY with RD set from a client
///     that may recurse (<see cref="RecursiveServerOptions.EnableRecursion" /> on, or an access
///     rule granting recursion access). A fake UDP upstream on loopback shows whether anything
///     was sent.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    /// <summary>
    ///     This method runs one query through a resolver whose only upstream is a good fake and
    ///     returns the resolution and the number of datagrams the fake received.
    /// </summary>
    private async Task<(DnsQueryResolution Resolution, int Sent)> ResolveForAccessAsync(
        DnsMessage request,
        bool enableRecursion,
        IDnsAccessControlEngine accessControlEngine = null
    )
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver(
            recursiveOptions: new RecursiveServerOptions
            {
                EnableDnssecValidation = false,
                EnableRecursion = enableRecursion,
            },
            accessControlEngine: accessControlEngine
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(BuildContext(request));

        return (resolution, upstream.QueryCount);
    }

    [Fact]
    public async Task TryResolveAsync_WithRecursionDesiredClear_ReturnsNotHandledAndSendsNothing()
    {
        DnsMessage request = BuildQuery(31, "example.com.");
        request.RecursionDesired = false;

        (DnsQueryResolution resolution, int sent) = await ResolveForAccessAsync(request, true);

        Assert.False(resolution.IsHandled);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task TryResolveAsync_WithAStatusOpcode_ReturnsNotHandledAndSendsNothing()
    {
        DnsMessage request = BuildQuery(32, "example.com.");
        request.Opcode = DnsOpcodeEnum.Status;

        (DnsQueryResolution resolution, int sent) = await ResolveForAccessAsync(request, true);

        Assert.False(resolution.IsHandled);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task TryResolveAsync_WithRecursionOffAndNoAccessEngine_ReturnsNotHandledAndSendsNothing()
    {
        (DnsQueryResolution resolution, int sent) = await ResolveForAccessAsync(
            BuildQuery(33, "example.com."),
            enableRecursion: false
        );

        Assert.False(resolution.IsHandled);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task TryResolveAsync_WithRecursionOffButARuleGrantingRecursionAccess_Forwards()
    {
        GrantingAccessControlEngine engine = new(IPAddress.Loopback);

        (DnsQueryResolution resolution, int sent) = await ResolveForAccessAsync(
            BuildQuery(34, "example.com."),
            enableRecursion: false,
            engine
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(1, sent);
    }

    [Fact]
    public async Task TryResolveAsync_WithRecursionOffAndARuleForAnotherAddress_ReturnsNotHandledAndSendsNothing()
    {
        GrantingAccessControlEngine engine = new(IPAddress.Parse("203.0.113.5"));

        (DnsQueryResolution resolution, int sent) = await ResolveForAccessAsync(
            BuildQuery(35, "example.com."),
            enableRecursion: false,
            engine
        );

        Assert.False(resolution.IsHandled);
        Assert.Equal(0, sent);
    }

    /// <summary>
    ///     This class is an access-control engine that grants only recursion access, and only to
    ///     one address, so a test can tell a granted client from any other without the real
    ///     access-control cache and repositories.
    /// </summary>
    private sealed class GrantingAccessControlEngine(IPAddress grantedAddress)
        : IDnsAccessControlEngine
    {
        /// <inheritdoc />
        public ValueTask<bool> HasCapabilityAsync(
            IPAddress remoteAddress,
            DnsAccessControlCapabilityEnum capability,
            CancellationToken stoppingToken = default
        ) =>
            ValueTask.FromResult(
                capability == DnsAccessControlCapabilityEnum.RecursionAccess
                    && remoteAddress.Equals(grantedAddress)
            );
    }
}

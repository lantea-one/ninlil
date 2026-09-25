using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class tests the ordered fallback of <see cref="CompositeClientIdentityResolver" />
///     with in-memory fake <see cref="IClientIdentityResolver" />s; no DHCP or ARP lookup is
///     involved.
/// </summary>
public sealed class CompositeClientIdentityResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenTheFirstResolverAnswers_NeverConsultsTheSecondOne()
    {
        FakeClientIdentityResolver first = new(
            new ClientIdentityResolution("aa:bb:cc:dd:ee:ff", InternetProtocolVersionEnum.IPv4)
        );
        FakeClientIdentityResolver second = new(
            new ClientIdentityResolution("11:22:33:44:55:66", InternetProtocolVersionEnum.IPv4)
        );

        CompositeClientIdentityResolver composite = new([first, second]);

        ClientIdentityResolution resolution = await composite.ResolveAsync(IPAddress.Loopback);

        Assert.Equal("aa:bb:cc:dd:ee:ff", resolution.ClientIdentifier);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheFirstResolverReturnsNull_FallsBackToTheSecondOne()
    {
        FakeClientIdentityResolver first = new(null);
        FakeClientIdentityResolver second = new(
            new ClientIdentityResolution("11:22:33:44:55:66", InternetProtocolVersionEnum.IPv4)
        );

        CompositeClientIdentityResolver composite = new([first, second]);

        ClientIdentityResolution resolution = await composite.ResolveAsync(IPAddress.Loopback);

        Assert.Equal("11:22:33:44:55:66", resolution.ClientIdentifier);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_WhenNoResolverAnswers_ReturnsNull()
    {
        CompositeClientIdentityResolver composite = new([
            new FakeClientIdentityResolver(null),
            new FakeClientIdentityResolver(null),
        ]);

        ClientIdentityResolution resolution = await composite.ResolveAsync(IPAddress.Loopback);

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This class is a fake resolver that always returns the same result and counts how often
    ///     it was called.
    /// </summary>
    private sealed class FakeClientIdentityResolver(ClientIdentityResolution result)
        : IClientIdentityResolver
    {
        public int CallCount { get; private set; }

        public Task<ClientIdentityResolution> ResolveAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        )
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}

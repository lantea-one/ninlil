using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class covers <see cref="CompositeClientIdentityResolver" />'s own ordered fallback
///     behavior against small, in-memory fake <see cref="IClientIdentityResolver" />
///     implementations -- no real DHCP or ARP mechanism is exercised here, only the composition
///     logic itself.
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
    ///     This class is a small, in-memory fake used only by this test class, always answering
    ///     the same fixed result and counting how many times it was consulted.
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

using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IClientIdentityResolver" />: it answers a
///     fixed, test-configured <see cref="ClientIdentityResolution" /> for one specific source
///     address and <see langword="null" /> for every other, mirroring
///     <c>Ninlil.Dns.Server.Filtering.Tests.Fakes.FakeClientIdentityResolver</c>'s own identical
///     fake and <c>Ninlil.Dns.Server.Core.Tests.ClientIdentity.CompositeClientIdentityResolverTests</c>'s
///     own original.
/// </summary>
public sealed class FakeClientIdentityResolver : IClientIdentityResolver
{
    private readonly IPAddress _matchAddress;
    private readonly ClientIdentityResolution _resolution;

    /// <summary>
    ///     This constructor configures which single source address this fake resolves, and what
    ///     it resolves that address to.
    /// </summary>
    /// <param name="matchAddress">The one source address this fake ever resolves.</param>
    /// <param name="resolution">The resolution to answer for <paramref name="matchAddress" />.</param>
    public FakeClientIdentityResolver(IPAddress matchAddress, ClientIdentityResolution resolution)
    {
        _matchAddress = matchAddress;
        _resolution = resolution;
    }

    /// <inheritdoc />
    public Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult(
            remoteAddress != null && remoteAddress.Equals(_matchAddress) ? _resolution : null
        );
}

using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

namespace Ninlil.Dns.Server.Filtering.Tests.Fakes;

/// <summary>
///     This class is a fake <see cref="IClientIdentityResolver" />: it returns a configured
///     <see cref="ClientIdentityResolution" /> for one source address and <see langword="null" />
///     for any other.
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
    ) => Task.FromResult(remoteAddress.Equals(_matchAddress) ? _resolution : null);
}

using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests.Fakes;

/// <summary>
///     This class is a fake <see cref="IClientIdentityResolver" />. It answers a fixed
///     <see cref="ClientIdentityResolution" /> for one source address and
///     <see langword="null" /> for any other, like the fake of the same name in
///     <c>Ninlil.Dns.Server.Filtering.Tests</c>.
/// </summary>
public sealed class FakeClientIdentityResolver : IClientIdentityResolver
{
    private readonly IPAddress _matchAddress;
    private readonly ClientIdentityResolution _resolution;

    /// <summary>
    ///     This constructor configures the source address and what it resolves to.
    /// </summary>
    /// <param name="matchAddress">The only source address this fake resolves.</param>
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

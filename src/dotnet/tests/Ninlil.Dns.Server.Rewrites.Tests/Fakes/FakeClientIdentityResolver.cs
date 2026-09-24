using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

namespace Ninlil.Dns.Server.Rewrites.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IClientIdentityResolver" />: it answers a
///     fixed, test-configured <see cref="ClientIdentityResolution" /> for a small, explicit set of
///     source addresses and <see langword="null" /> for every other, mirroring
///     <c>Ninlil.Dns.Server.ClientSchedule.Tests.Fakes.FakeClientIdentityResolver</c>'s own nearly
///     identical fake (extended here to support more than one configured address at once, since
///     several of this project's own scoping tests need to resolve two distinct clients within the
///     same test).
/// </summary>
public sealed class FakeClientIdentityResolver : IClientIdentityResolver
{
    private readonly Dictionary<IPAddress, ClientIdentityResolution> _resolutions = [];

    /// <summary>
    ///     This method configures which single source address this fake resolves to which
    ///     resolution, returning this same instance for fluent chaining across several addresses.
    /// </summary>
    public FakeClientIdentityResolver WithResolution(
        IPAddress matchAddress,
        ClientIdentityResolution resolution
    )
    {
        _resolutions[matchAddress] = resolution;
        return this;
    }

    /// <inheritdoc />
    public Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult(
            remoteAddress is not null && _resolutions.TryGetValue(remoteAddress, out var resolution)
                ? resolution
                : null
        );
}

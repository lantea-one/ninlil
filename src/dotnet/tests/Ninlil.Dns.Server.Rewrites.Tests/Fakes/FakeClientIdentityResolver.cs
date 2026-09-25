using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

namespace Ninlil.Dns.Server.Rewrites.Tests.Fakes;

/// <summary>
///     This class is a fake <see cref="IClientIdentityResolver" />: it returns a configured
///     <see cref="ClientIdentityResolution" /> for each address set up with
///     <see cref="WithResolution" /> and <see langword="null" /> for any other. It supports several
///     addresses because some scoping tests resolve two clients in one test.
/// </summary>
public sealed class FakeClientIdentityResolver : IClientIdentityResolver
{
    private readonly Dictionary<IPAddress, ClientIdentityResolution> _resolutions = [];

    /// <summary>
    ///     This method maps one source address to a resolution and returns this instance, so calls
    ///     can be chained.
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

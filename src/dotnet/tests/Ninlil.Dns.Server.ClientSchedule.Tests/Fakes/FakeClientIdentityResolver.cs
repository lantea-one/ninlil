using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

namespace Ninlil.Dns.Server.ClientSchedule.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IClientIdentityResolver" />: it answers a
///     fixed, test-configured <see cref="ClientIdentityResolution" /> for one or more specific
///     source addresses and <see langword="null" /> for every other, mirroring
///     <c>Ninlil.Dns.Server.Filtering.Tests.Fakes.FakeClientIdentityResolver</c>'s own identical
///     fake.
/// </summary>
public sealed class FakeClientIdentityResolver : IClientIdentityResolver
{
    private readonly Dictionary<IPAddress, ClientIdentityResolution> _resolutions;

    /// <summary>
    ///     This constructor configures which single source address this fake resolves, and what
    ///     it resolves that address to.
    /// </summary>
    /// <param name="matchAddress">The one source address this fake ever resolves.</param>
    /// <param name="resolution">The resolution to answer for <paramref name="matchAddress" />.</param>
    public FakeClientIdentityResolver(IPAddress matchAddress, ClientIdentityResolution resolution)
        : this(new Dictionary<IPAddress, ClientIdentityResolution> { [matchAddress] = resolution })
    { }

    /// <summary>
    ///     This constructor configures more than one address-to-resolution mapping at once --
    ///     used by a test proving one client's own configuration has no bearing on a genuinely
    ///     different, unrelated client resolved from a different source address.
    /// </summary>
    /// <param name="resolutions">Every source address this fake resolves, and what each resolves to.</param>
    public FakeClientIdentityResolver(
        IReadOnlyDictionary<IPAddress, ClientIdentityResolution> resolutions
    ) => _resolutions = new Dictionary<IPAddress, ClientIdentityResolution>(resolutions);

    /// <inheritdoc />
    public Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult(
            _resolutions.TryGetValue(remoteAddress, out ClientIdentityResolution resolution)
                ? resolution
                : null
        );
}

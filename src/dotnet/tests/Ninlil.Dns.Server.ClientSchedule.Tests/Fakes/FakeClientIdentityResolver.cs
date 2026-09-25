using System.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests.Fakes;

/// <summary>
///     This class is a fake <see cref="IClientIdentityResolver" />. It answers a fixed
///     <see cref="ClientIdentityResolution" /> for the source addresses a test configures and
///     <see langword="null" /> for any other. It matches the fake of the same name in
///     <c>Ninlil.Dns.Server.Filtering.Tests</c>.
/// </summary>
public sealed class FakeClientIdentityResolver : IClientIdentityResolver
{
    private readonly Dictionary<IPAddress, ClientIdentityResolution> _resolutions;

    /// <summary>
    ///     This constructor configures one source address and what it resolves to.
    /// </summary>
    /// <param name="matchAddress">The only source address this fake resolves.</param>
    /// <param name="resolution">The resolution to answer for <paramref name="matchAddress" />.</param>
    public FakeClientIdentityResolver(IPAddress matchAddress, ClientIdentityResolution resolution)
        : this(new Dictionary<IPAddress, ClientIdentityResolution> { [matchAddress] = resolution })
    { }

    /// <summary>
    ///     This constructor configures several addresses at once, for tests that check one
    ///     client's settings do not affect another client.
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

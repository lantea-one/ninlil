using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Cluster.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class turns clustering on in the shared test host for the length of one test, and
///     restores the previous values when disposed. A settings write cannot do this, because the
///     factory's runtime settings file is not a configuration source of the host (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />). Instead it
///     changes the cached <see cref="ClusterOptions" /> instance that
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> hands to every service. That instance
///     stays cached because nothing reloads the cluster section during a test run, and the tests
///     in <see cref="WebTestCollection" /> run one at a time, so no other test sees the change.
///     <see cref="ClusterOptions.AdvertisedPort" /> is left alone, so it keeps its default of 0.
/// </summary>
public sealed class LiveClusterOptionsScope : IDisposable
{
    /// <summary>
    ///     This field contains the cached options instance this scope changes.
    /// </summary>
    private readonly ClusterOptions _options;

    /// <summary>
    ///     This field contains the value of <see cref="ClusterOptions.Enable" /> to restore.
    /// </summary>
    private readonly bool _previousEnable;

    /// <summary>
    ///     This field contains the value of <see cref="ClusterOptions.AdvertisedHost" /> to restore.
    /// </summary>
    private readonly string _previousAdvertisedHost;

    /// <summary>
    ///     This constructor enables clustering with the given advertised host.
    /// </summary>
    /// <param name="factory">The shared test host.</param>
    /// <param name="advertisedHost">The advertised host to set; empty leaves it unset.</param>
    public LiveClusterOptionsScope(NinlilDnsWebApplicationFactory factory, string advertisedHost)
    {
        _options = factory
            .Services.GetRequiredService<IOptionsMonitor<ClusterOptions>>()
            .CurrentValue;
        _previousEnable = _options.Enable;
        _previousAdvertisedHost = _options.AdvertisedHost;

        _options.Enable = true;
        _options.AdvertisedHost = advertisedHost;
    }

    /// <summary>
    ///     This property contains the live options, for a test that reads the node id or port.
    /// </summary>
    public ClusterOptions Options => _options;

    /// <inheritdoc />
    public void Dispose()
    {
        _options.Enable = _previousEnable;
        _options.AdvertisedHost = _previousAdvertisedHost;
    }
}

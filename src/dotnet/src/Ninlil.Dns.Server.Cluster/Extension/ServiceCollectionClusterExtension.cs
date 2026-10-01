using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Cluster.Authentication;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Heartbeat;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Invite;
using Ninlil.Dns.Server.Cluster.Join;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's clustering: <see cref="ClusterOptions" />, the invite and join services, the peer
///     HTTP client factory, the heartbeat service, and the config-sync broadcaster, username
///     checker and statistics aggregator. The <see cref="ClusterTsigAuthenticationDefaults.AuthenticationScheme" />
///     scheme is registered separately by <see cref="AddNinlilDnsServerClusterAuthentication" />,
///     which <c>Program.cs</c> calls after setting up the other authentication schemes.
/// </summary>
public static class ServiceCollectionClusterExtension
{
    /// <summary>
    ///     This method registers the clustering services other than authentication. The
    ///     composition root must also register
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IClusterNodeRepository" /> and
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.ITsigKeyRepository" />, and call
    ///     <c>AddNinlilDnsServerTsig</c> for <c>TsigSecretProtector</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read cluster settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerCluster(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<ClusterOptions>(configuration.GetSection(ClusterOptions.SectionName));

        instance.AddSingleton<ClusterPeerHttpClientFactory>();
        instance.AddScoped<ClusterInviteService>();
        instance.AddScoped<ClusterJoinService>();

        instance.AddHostedService<ClusterHeartbeatBackgroundService>();

        // The config-sync broadcaster, registered against the Server.Core abstraction so the
        // event subscribers in Ninlil.Dns.Server.ClusterSync do not depend on this project.
        // Singleton, like the peer HTTP client factory; it keeps no per-request state and
        // reads peers through its own scope.
        instance.AddSingleton<IClusterConfigSyncBroadcaster, ClusterConfigSyncBroadcaster>();

        // The best-effort cluster-wide username check (see IClusterUsernameUniquenessChecker).
        instance.AddSingleton<
            IClusterUsernameUniquenessChecker,
            ClusterUsernameUniquenessChecker
        >();

        // The cluster dashboard statistics collector (see IClusterQueryStatsAggregator).
        instance.AddSingleton<IClusterQueryStatsAggregator, ClusterQueryStatsAggregator>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method registers the <see cref="ClusterTsigAuthenticationDefaults.AuthenticationScheme" />
    ///     scheme. It is called after <c>AddNinlilDnsAuthentication</c>: the parameterless
    ///     <c>AddAuthentication()</c> returns a builder over the existing configuration, so this
    ///     adds one scheme without changing the default scheme or the JWT/API-token selection.
    ///     The scheme is not part of that selection; cluster-channel actions name it in their
    ///     <c>[Authorize(AuthenticationSchemes = ...)]</c> attribute, so an operator's token cannot
    ///     reach a peer endpoint and a peer signature cannot reach an operator endpoint.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerClusterAuthentication(
        this IServiceCollection instance
    )
    {
        instance
            .AddAuthentication()
            .AddScheme<ClusterTsigAuthenticationSchemeOptions, ClusterTsigAuthenticationHandler>(
                ClusterTsigAuthenticationDefaults.AuthenticationScheme,
                _ => { }
            );

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}

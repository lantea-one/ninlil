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
///     Ninlil's cluster membership foundation: <see cref="ClusterOptions" />, the invite/join
///     handshake services, the outbound peer HTTP client factory, and the background heartbeat
///     engine. It deliberately does <b>not</b> register the <see cref="ClusterTsigAuthenticationDefaults.AuthenticationScheme" />
///     authentication scheme itself -- that needs to be layered onto the very same
///     <see cref="Microsoft.AspNetCore.Authentication.AuthenticationBuilder" /> the composition
///     root's own <c>AddNinlilDnsAuthentication</c> call already built, which only the
///     composition root (<c>Ninlil.Web</c>'s own <c>Program.cs</c>) has a handle to; see
///     <see cref="AddNinlilDnsServerClusterAuthentication" /> for that half.
/// </summary>
public static class ServiceCollectionClusterExtension
{
    /// <summary>
    ///     This method registers every non-authentication service Phase 9-6a's cluster
    ///     membership foundation needs. The composition root is still expected to have already
    ///     registered an <see cref="Ninlil.Dns.Data.Abstractions.Repository.IClusterNodeRepository" />
    ///     and an <see cref="Ninlil.Dns.Data.Abstractions.Repository.ITsigKeyRepository" />
    ///     implementation, and to have already called <c>AddNinlilDnsServerTsig</c> for
    ///     <c>TsigSecretProtector</c>.
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

        // Phase 9-6c's operational config sync broadcaster -- registered here, against Core's
        // own IClusterConfigSyncBroadcaster abstraction, so every per-category event subscriber
        // (living in Ninlil.Dns.Server.ClusterSync) can depend on that abstraction alone, never
        // on this project directly. Singleton, matching ClusterPeerHttpClientFactory's own
        // lifetime -- both are called from singleton-lifetime event subscribers/hosted services.
        instance.AddSingleton<IClusterConfigSyncBroadcaster, ClusterConfigSyncBroadcaster>();

        // Phase 9-6d's own best-effort, cluster-wide username-uniqueness check -- see
        // IClusterUsernameUniquenessChecker's own doc comment. Singleton, the same reasoning as
        // IClusterConfigSyncBroadcaster immediately above.
        instance.AddSingleton<
            IClusterUsernameUniquenessChecker,
            ClusterUsernameUniquenessChecker
        >();

        // Phase 9-6e's own aggregate cluster dashboard query-stats collector -- see
        // IClusterQueryStatsAggregator's own doc comment. Singleton, the same reasoning as every
        // other cluster-channel caller above.
        instance.AddSingleton<IClusterQueryStatsAggregator, ClusterQueryStatsAggregator>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method registers the <see cref="ClusterTsigAuthenticationDefaults.AuthenticationScheme" />
    ///     scheme onto the application's own existing authentication builder. Called separately
    ///     from <see cref="AddNinlilDnsServerCluster" />, and after <c>AddNinlilDnsAuthentication</c>
    ///     has already run, because ASP.NET Core's own <c>AddAuthentication()</c> no-argument
    ///     overload simply returns a builder wrapping whatever authentication configuration
    ///     already exists rather than resetting it -- calling it again here, with no configure
    ///     delegate of its own, adds this one additional scheme without touching the
    ///     <c>DefaultScheme</c>/<c>DefaultChallengeScheme</c>/JWT-or-API-token forwarding
    ///     selection <c>ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication</c>
    ///     already configured. This scheme is deliberately never part of that forwarding
    ///     selection at all: every cluster-channel controller action names it explicitly via its
    ///     own <c>[Authorize(AuthenticationSchemes = ClusterTsigAuthenticationDefaults.AuthenticationScheme)]</c>
    ///     attribute, so a human operator's JWT can never authenticate against a peer-only
    ///     endpoint, and a peer's TSIG signature can never authenticate against a human-operator
    ///     endpoint.
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

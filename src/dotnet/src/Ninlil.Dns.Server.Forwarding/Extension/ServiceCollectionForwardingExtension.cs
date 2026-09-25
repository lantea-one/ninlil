using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     upstream forwarding. It carries the same <see cref="SupportedOSPlatformAttribute" /> set
///     as <see cref="QuicForwarderClient" />, since it registers that client.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public static class ServiceCollectionForwardingExtension
{
    /// <summary>
    ///     This method binds <see cref="ForwardingOptions" /> and registers every forwarder client,
    ///     the definition parser, the health tracker, the forwarded-response cache, and
    ///     <see cref="ForwarderQueryResolver" /> as an <see cref="IDnsQueryResolver" />. All are
    ///     registered even when <see cref="ForwardingOptions.Enable" /> is off at startup, so
    ///     forwarding can be turned on without a restart (a fresh install starts with it off). A
    ///     disabled resolver checks the flag first and returns
    ///     <see cref="DnsQueryResolution.NotHandled" /> at the cost of one field read.
    ///     <para>
    ///         <b>Registration order matters.</b> <see cref="DnsQueryDispatcher" /> tries
    ///         <see cref="IDnsQueryResolver" /> implementations in registration order, and
    ///         forwarding sits between authoritative answers and recursive iteration. So
    ///         <c>Program.cs</c> in <c>Ninlil.Web</c> must call this method after
    ///         <c>AddNinlilDnsServerAuthoritative</c> and before
    ///         <c>AddNinlilDnsServerRecursive</c>.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read forwarding settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerForwarding(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<ForwardingOptions>(
            configuration.GetSection(ForwardingOptions.SectionName)
        );

        instance.AddSingleton<
            IUpstreamForwarderDefinitionParser,
            UpstreamForwarderDefinitionParser
        >();
        instance.AddSingleton<IUpstreamForwarderClient, PlainForwarderClient>();
        instance.AddSingleton<IUpstreamForwarderClient, TlsForwarderClient>();
        instance.AddSingleton<IUpstreamForwarderClient, HttpsForwarderClient>();
        instance.AddSingleton<IUpstreamForwarderClient, QuicForwarderClient>();
        instance.AddSingleton<IUpstreamForwarderClient, DnsCryptForwarderClient>();

        instance.AddSingleton<ForwarderHealthTracker>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<ForwarderHealthTracker>()
        );

        // One process-wide forwarded-response cache, registered under both its resolver-facing
        // interface and IDnsCache, so Server.Core's DnsCacheEvictionService sweeps it and the
        // cache REST endpoint can report and flush it alongside the recursive cache.
        instance.AddSingleton<ForwardedResponseCache>();
        instance.AddSingleton<IForwardedResponseCache>(provider =>
            provider.GetRequiredService<ForwardedResponseCache>()
        );
        instance.AddSingleton<IDnsCache>(provider =>
            provider.GetRequiredService<ForwardedResponseCache>()
        );

        instance.AddSingleton<IDnsQueryResolver, ForwarderQueryResolver>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}

using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's upstream forwarding. Carries the same
///     <see cref="SupportedOSPlatformAttribute" /> set as <see cref="QuicForwarderClient" />
///     itself, since this class is what registers it.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public static class ServiceCollectionForwardingExtension
{
    /// <summary>
    ///     This method registers every forwarder client, the definition parser, the health
    ///     tracker, and <see cref="ForwarderQueryResolver" /> itself as an
    ///     <see cref="IDnsQueryResolver" /> -- all unconditionally, regardless of
    ///     <see cref="ForwardingOptions.Enable" />'s own value at process startup. Registering
    ///     <see cref="ForwarderQueryResolver" /> only when already enabled at startup (this
    ///     method's own original shape) would mean a fresh install -- which starts with
    ///     forwarding off by design -- could never turn it on live at all, the same structural gap
    ///     this project's own DNS-over-HTTPS route-mapping registration had to be fixed for.
    ///     <see cref="ForwarderQueryResolver" /> itself already carries the live
    ///     <see cref="ForwardingOptions.Enable" /> check internally (via
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, its own
    ///     <c>TryResolveAsync</c>'s very first check), so a disabled resolver costs one field read
    ///     per query, immediately returning <see cref="DnsQueryResolution.NotHandled" />, never a
    ///     database round trip.
    ///     <para>
    ///         <b>Required registration order:</b> the real composition root (<c>Ninlil.Web</c>'s
    ///         own <c>Program.cs</c>) must call this method <em>between</em>
    ///         <c>Server.Authoritative</c>'s own <c>AddNinlilDnsServerAuthoritative</c> and
    ///         <c>Server.Recursive</c>'s own <c>AddNinlilDnsServerRecursive</c> --
    ///         <see cref="IDnsQueryResolver" /> implementations are tried by
    ///         <see cref="DnsQueryDispatcher" /> in dependency-injection registration
    ///         order, and the "forwarder-first, iterate on failure" design decision depends on
    ///         authoritative winning first, this resolver second, and recursive iteration only as
    ///         the final fallback -- true regardless of whether forwarding is currently enabled,
    ///         since this resolver is now always registered.
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

        instance.AddSingleton<IDnsQueryResolver, ForwarderQueryResolver>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}

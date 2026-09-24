using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Extension;

/// <summary>
///     This class provides our <see cref="IEndpointRouteBuilder" /> extension for mapping RFC
///     8484's DNS-over-HTTPS transport onto an already-running host's own Kestrel instance --
///     the alternative to <see cref="DnsOverHttpsHostedService" />'s own self-hosted Kestrel,
///     for a composition root (<c>Ninlil.Web</c>) that already has one HTTP(S) listener and
///     does not want a second one competing for the same port range in the same process.
/// </summary>
public static class EndpointRouteBuilderDnsOverHttpsExtension
{
    /// <summary>
    ///     This method maps RFC 8484 §4.1's GET and POST forms at
    ///     <see cref="DnsOverHttpsOptions.Path" />, <b>unconditionally</b> -- regardless of
    ///     <see cref="DnsOverHttpsOptions.Enable" />'s own value at the moment this method runs.
    ///     <para>
    ///         This is deliberate, not an oversight: ASP.NET Core's Minimal API routing has no
    ///         supported "map this route later" operation, so a route left unmapped while
    ///         <see cref="DnsOverHttpsOptions.Enable" /> was <see langword="false" /> at process
    ///         startup could never be added afterward without a full process restart -- silently
    ///         defeating a later REST-driven <c>enable</c> toggle
    ///         (<c>Ninlil.Web</c>'s own transport settings controller) for exactly the
    ///         installations that start with this transport off, this project's own documented
    ///         default. Mapping the route always and gating every request through
    ///         <see cref="DnsOverHttpsTransportGate" /> instead -- opened here only when
    ///         <see cref="DnsOverHttpsOptions.Enable" /> starts <see langword="true" />, and
    ///         opened/closed afterward only by that gate's own <c>StartAsync</c>/<c>StopAsync</c> --
    ///         is what makes toggling this transport on or off genuinely live in both directions,
    ///         with no restart, matching every other transport's own real socket-level start/stop.
    ///     </para>
    ///     <para>
    ///         Only <see cref="DnsOverHttpsOptions.Enable" />, <see cref="DnsOverHttpsOptions.Path" />,
    ///         and <see cref="DnsOverHttpsOptions.MaxMessageSizeBytes" /> apply when mapped this
    ///         way -- <see cref="DnsOverHttpsOptions.Port" /> and
    ///         <see cref="DnsOverHttpsOptions.Certificate" /> are specific to
    ///         <see cref="DnsOverHttpsHostedService" />'s own self-hosted Kestrel instance and are
    ///         silently ignored here; the shared host's own Kestrel configuration (its own port
    ///         and certificate) decides both instead.
    ///     </para>
    ///     <para>
    ///         Also maps a second pair of routes, one path segment longer
    ///         (<c>{options.Path}/{{clientid}}</c>, e.g. <c>/dns-query/{clientid}</c>), for
    ///         encrypted-DNS client identification -- a client that wants its own answers
    ///         evaluated against a specific <c>DnsClientGroupEntity</c> (rather than whichever
    ///         group its own source address falls into) queries through this path instead, with
    ///         its own chosen ClientID as the trailing segment. The bare route mapped above is left
    ///         completely unchanged and keeps answering exactly as before for a client that never
    ///         sends one.
    ///     </para>
    /// </summary>
    /// <param name="endpoints">The current <see cref="IEndpointRouteBuilder" /> instance.</param>
    /// <returns>The modified <paramref name="endpoints" /> instance.</returns>
    public static IEndpointRouteBuilder MapNinlilDnsOverHttps(this IEndpointRouteBuilder endpoints)
    {
        DnsOverHttpsOptions options = endpoints
            .ServiceProvider.GetRequiredService<IOptions<DnsOverHttpsOptions>>()
            .Value;

        DnsQueryProcessingPipeline pipeline =
            endpoints.ServiceProvider.GetRequiredService<DnsQueryProcessingPipeline>();

        // This mapping has no socket of its own to bind/unbind -- it rides the shared host's own
        // Kestrel instance, which is always already running by the time this method runs. The
        // gate is this transport's own substitute for that: opened here, right after mapping,
        // only when this transport starts enabled -- exactly like the generic host opening every
        // other transport's socket at startup only when its own Enable flag says to -- and
        // closed/reopened later by ServicesController's own stop/start actions, or by
        // TransportSettingsController's own enable toggle, either way through the exact same gate
        // instance (see DnsOverHttpsTransportGate's own doc comment for why a gate, not a real
        // unmap, is necessary here at all).
        DnsOverHttpsTransportGate gate =
            endpoints.ServiceProvider.GetRequiredService<DnsOverHttpsTransportGate>();

        if (options.Enable)
            gate.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        endpoints.MapGet(
            options.Path,
            context => DnsOverHttpsRequestHandler.HandleGetAsync(context, pipeline, options, gate)
        );
        endpoints.MapPost(
            options.Path,
            context => DnsOverHttpsRequestHandler.HandlePostAsync(context, pipeline, options, gate)
        );

        string clientIdPath = options.Path.TrimEnd('/') + "/{clientid}";

        endpoints.MapGet(
            clientIdPath,
            context =>
                DnsOverHttpsRequestHandler.HandleGetAsync(
                    context,
                    pipeline,
                    options,
                    gate,
                    (string)context.Request.RouteValues["clientid"]
                )
        );
        endpoints.MapPost(
            clientIdPath,
            context =>
                DnsOverHttpsRequestHandler.HandlePostAsync(
                    context,
                    pipeline,
                    options,
                    gate,
                    (string)context.Request.RouteValues["clientid"]
                )
        );

        return endpoints;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Extension;

/// <summary>
///     This class provides our <see cref="IEndpointRouteBuilder" /> extension for mapping DNS
///     over HTTPS (RFC 8484) onto the web host's Kestrel server. <c>Ninlil.Web</c> uses it instead
///     of <see cref="DnsOverHttpsHostedService" />, so the process has one HTTPS listener, not
///     two.
/// </summary>
public static class EndpointRouteBuilderDnsOverHttpsExtension
{
    /// <summary>
    ///     This method maps the GET and POST forms of RFC 8484 §4.1 at
    ///     <see cref="DnsOverHttpsOptions.Path" />, <b>whatever</b> the value of
    ///     <see cref="DnsOverHttpsOptions.Enable" />.
    ///     <para>
    ///         Minimal API routing cannot add a route after startup, so a route skipped because the
    ///         transport started disabled (the default) could never be added without a restart,
    ///         and enabling it through <c>Ninlil.Web</c>'s transport settings controller would do
    ///         nothing. Instead the route is always mapped and every request goes through
    ///         <see cref="DnsOverHttpsTransportGate" />, which is opened here only when the
    ///         transport starts enabled and is opened and closed afterwards through its
    ///         <c>StartAsync</c>/<c>StopAsync</c>. That makes enabling and disabling work without a
    ///         restart, like the socket-level start and stop of the other transports.
    ///     </para>
    ///     <para>
    ///         Only <see cref="DnsOverHttpsOptions.Enable" />, <see cref="DnsOverHttpsOptions.Path" />
    ///         and <see cref="DnsOverHttpsOptions.MaxMessageSizeBytes" /> apply here, and the last
    ///         two are read once, when the route is mapped. <see cref="DnsOverHttpsOptions.Port" />
    ///         and <see cref="DnsOverHttpsOptions.Certificate" /> are ignored; the web host's
    ///         Kestrel configuration sets the port and certificate.
    ///     </para>
    ///     <para>
    ///         It also maps a second pair of routes one segment longer
    ///         (<c>{options.Path}/{{clientid}}</c>, e.g. <c>/dns-query/{clientid}</c>). A client
    ///         that wants its queries matched to a particular <c>DnsClientGroupEntity</c>, rather
    ///         than the group its source address falls into, puts its ClientID in that last
    ///         segment. The bare route works as before for clients that send none.
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

        // This mapping has no socket to bind; it uses the web host's Kestrel server. The gate
        // takes the socket's place: it is opened here only when the transport starts enabled,
        // as the other transports bind only when enabled, and is later closed and reopened by
        // ServicesController's stop/start actions or TransportSettingsController's enable
        // toggle, all through this one singleton (see DnsOverHttpsTransportGate for why).
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

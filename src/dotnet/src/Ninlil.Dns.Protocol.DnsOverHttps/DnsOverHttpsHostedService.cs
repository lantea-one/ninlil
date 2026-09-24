using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class implements RFC 8484's DNS-over-HTTPS transport by self-hosting its own small,
///     separate Kestrel server -- for standalone use of this project without
///     <c>Ninlil.Web</c>. The real web host instead maps the same two forms directly onto
///     its own, already-running Kestrel instance via
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" />, sharing one process
///     and one certificate with the REST API rather than running a second, separate Kestrel
///     instance in the same process; both share the actual request-handling logic in
///     <see cref="DnsOverHttpsRequestHandler" /> so they can never drift apart. This class itself
///     answers both forms RFC 8484 §4.1 defines for a "DoH stub resolver": GET, with the query
///     base64url-encoded in a <c>dns</c> query-string parameter, and POST, with the raw
///     wire-format message as the request body.
///     <para>
///         The nested Kestrel host started here runs its own, separate default logging pipeline
///         rather than sharing this project's outer one -- a deliberate simplification, not a
///         bug, since unifying logging across a nested-host boundary needs plumbing this
///         standalone-use path does not otherwise need.
///     </para>
/// </summary>
/// <param name="pipeline">The pipeline every query is enqueued onto.</param>
/// <param name="options">This transport's own listen port, path, and certificate.</param>
/// <param name="logger">The logger this service records its own certificate loading and startup/shutdown to.</param>
public sealed class DnsOverHttpsHostedService(
    DnsQueryProcessingPipeline pipeline,
    IOptions<DnsOverHttpsOptions> options,
    ILogger<DnsOverHttpsHostedService> logger
) : IHostedService
{
    /// <summary>
    ///     This property contains the nested Kestrel host, once <see cref="StartAsync" /> has
    ///     built and started one.
    /// </summary>
    private WebApplication _app;

    /// <summary>
    ///     This property contains the server certificate presented to every connecting client --
    ///     re-checked periodically against disk, so a certificate an operator or
    ///     <c>Ninlil.Dns.Server.Acme</c> replaces mid-run is picked up without a restart.
    /// </summary>
    private ReloadableTlsCertificateProvider _certificateProvider;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
            return;

        _certificateProvider = new ReloadableTlsCertificateProvider(
            options.Value.Certificate,
            logger,
            options.Value.Certificate.CertificateReloadPollInterval
        );

        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // A DNS message can never legally exceed 65535 bytes; bounding Kestrel's own
            // request body limit to just above that means a client cannot make this transport
            // buffer an arbitrarily large body before MaxMessageSizeBytes below ever gets a
            // chance to reject it.
            kestrel.Limits.MaxRequestBodySize = options.Value.MaxMessageSizeBytes + 1024;

            kestrel.Listen(
                IPAddress.IPv6Any,
                options.Value.Port,
                listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    listenOptions.UseHttps(httpsOptions =>
                    {
                        httpsOptions.ServerCertificateSelector = (_, _) =>
                            _certificateProvider.Current;
                        httpsOptions.SslProtocols = options.Value.Certificate.AllowedProtocols;
                    });
                }
            );
        });

        WebApplication app = builder.Build();

        app.MapGet(
            options.Value.Path,
            context => DnsOverHttpsRequestHandler.HandleGetAsync(context, pipeline, options.Value)
        );
        app.MapPost(
            options.Value.Path,
            context => DnsOverHttpsRequestHandler.HandlePostAsync(context, pipeline, options.Value)
        );

        // A second pair of routes, one path segment longer, for encrypted-DNS client
        // identification -- see EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps's
        // own doc comment for the full reasoning; kept identical here so the standalone,
        // self-hosted-Kestrel mode never drifts from the shared-Kestrel mapping.
        string clientIdPath = options.Value.Path.TrimEnd('/') + "/{clientid}";

        app.MapGet(
            clientIdPath,
            context =>
                DnsOverHttpsRequestHandler.HandleGetAsync(
                    context,
                    pipeline,
                    options.Value,
                    clientId: (string)context.Request.RouteValues["clientid"]
                )
        );
        app.MapPost(
            clientIdPath,
            context =>
                DnsOverHttpsRequestHandler.HandlePostAsync(
                    context,
                    pipeline,
                    options.Value,
                    clientId: (string)context.Request.RouteValues["clientid"]
                )
        );

        await app.StartAsync(cancellationToken);
        _app = app;

        logger.LogInformation(
            "DNS-over-HTTPS listening on port {Port}, path '{Path}'.",
            options.Value.Port,
            options.Value.Path
        );
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
            await _app.StopAsync(cancellationToken);

        _certificateProvider?.Dispose();
    }
}

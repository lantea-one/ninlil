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
///     This class implements DNS over HTTPS (RFC 8484) on a small Kestrel server of its own, for
///     using this project without <c>Ninlil.Web</c>. The web host does not register it; it maps
///     the same routes onto its own Kestrel server with
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" />, sharing the port and
///     certificate of the API. Both use <see cref="DnsOverHttpsRequestHandler" />, so they answer
///     the same way: the GET form of RFC 8484 §4.1 (the query base64url-encoded in a <c>dns</c>
///     query-string parameter) and the POST form (the wire-format message as the body).
///     <para>
///         The Kestrel host started here has its own default logging rather than sharing the
///         outer host's; connecting the two is not worth the plumbing for this standalone use.
///     </para>
/// </summary>
/// <param name="pipeline">The pipeline every query is enqueued onto.</param>
/// <param name="options">The port, path, message size limit and certificate, read at start.</param>
/// <param name="logger">The logger used for certificate loading and startup.</param>
public sealed class DnsOverHttpsHostedService(
    DnsQueryProcessingPipeline pipeline,
    IOptions<DnsOverHttpsOptions> options,
    ILogger<DnsOverHttpsHostedService> logger
) : IHostedService
{
    /// <summary>
    ///     This field contains the Kestrel host, once <see cref="StartAsync" /> has built and
    ///     started it.
    /// </summary>
    private WebApplication _app;

    /// <summary>
    ///     This field contains the provider of the server certificate presented to every client.
    ///     It re-checks the certificate on disk periodically, so one an operator or
    ///     <c>Ninlil.Dns.Server.Acme</c> replaces is picked up without a restart.
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
            // Cap the request body just above MaxMessageSizeBytes, so a client cannot make the
            // server buffer a huge body before the size check rejects it.
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

        // The ClientID routes, one segment longer, as in
        // EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps (see there).
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

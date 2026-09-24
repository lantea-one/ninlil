using System.Buffers.Text;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class implements RFC 8484 §4.1's actual request handling -- both the GET form (the
///     query base64url-encoded in a <c>dns</c> query-string parameter) and the POST form (the
///     raw wire-format message as the request body) -- shared by both
///     <see cref="DnsOverHttpsHostedService" /> (which still self-hosts its own separate Kestrel
///     instance for standalone use) and
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" /> (which maps these same
///     two forms onto an already-running host's own Kestrel instance instead), so the two never
///     drift out of sync with each other.
/// </summary>
internal static class DnsOverHttpsRequestHandler
{
    /// <summary>
    ///     This constant contains RFC 8484 §6's required media type for a wire-format DNS
    ///     message carried over HTTP, both received (POST) and sent (both forms).
    /// </summary>
    private const string DnsMessageContentType = "application/dns-message";

    /// <summary>
    ///     This method answers RFC 8484 §4.1's GET form.
    /// </summary>
    /// <param name="context">The current HTTP request/response.</param>
    /// <param name="pipeline">The pipeline the decoded query is enqueued onto.</param>
    /// <param name="options">This transport's own path and maximum message size.</param>
    /// <param name="gate">
    ///     An optional live start/stop signal -- see <see cref="DnsOverHttpsTransportGate" />'s own
    ///     doc comment. Omitted (the default) for <see cref="DnsOverHttpsHostedService" />'s own
    ///     self-hosted Kestrel, which already has a genuine socket-level stop of its own and
    ///     therefore needs no separate gate; supplied whenever this handler is reached through
    ///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />'s
    ///     own shared-Kestrel mapping instead, which has no such mechanism of its own.
    /// </param>
    /// <param name="clientId">
    ///     The <c>{clientid}</c> URL-path segment this request carried, or <see langword="null" />
    ///     for a request against the bare <c>options.Path</c> route (no ClientID at all) -- see
    ///     <see cref="Server.Core.Pipeline.DnsInboundMessage.ClientId" />'s own doc comment for
    ///     what this identifier is used for downstream.
    /// </param>
    public static Task HandleGetAsync(
        HttpContext context,
        DnsQueryProcessingPipeline pipeline,
        DnsOverHttpsOptions options,
        DnsOverHttpsTransportGate gate = null,
        string clientId = null
    )
    {
        if (gate is not null && !gate.IsRunning)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        }

        string encoded = context.Request.Query["dns"];

        if (string.IsNullOrEmpty(encoded))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        byte[] requestBytes;

        try
        {
            requestBytes = Base64Url.DecodeFromChars(encoded);
        }
        catch (FormatException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        return ResolveAndRespondAsync(context, pipeline, options, requestBytes, clientId);
    }

    /// <summary>
    ///     This method answers RFC 8484 §4.1's POST form.
    /// </summary>
    /// <param name="context">The current HTTP request/response.</param>
    /// <param name="pipeline">The pipeline the decoded query is enqueued onto.</param>
    /// <param name="options">This transport's own path and maximum message size.</param>
    /// <param name="gate">
    ///     An optional live start/stop signal -- see <see cref="HandleGetAsync" />'s own
    ///     <paramref name="gate" /> doc comment for the full explanation.
    /// </param>
    /// <param name="clientId">The <c>{clientid}</c> URL-path segment this request carried -- see <see cref="HandleGetAsync" />'s own <paramref name="clientId" /> doc comment.</param>
    public static async Task HandlePostAsync(
        HttpContext context,
        DnsQueryProcessingPipeline pipeline,
        DnsOverHttpsOptions options,
        DnsOverHttpsTransportGate gate = null,
        string clientId = null
    )
    {
        if (gate is not null && !gate.IsRunning)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (context.Request.ContentType != DnsMessageContentType)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        // A DNS message can never legally exceed 65535 bytes; bounding the request body limit
        // to just above that (scoped to only this request, not the whole shared host's own
        // default) means a client cannot make this handler buffer an arbitrarily large body
        // before the MaxMessageSizeBytes check inside ResolveAndRespondAsync below ever gets a
        // chance to reject it.
        context.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize =
            options.MaxMessageSizeBytes + 1024;

        using MemoryStream buffer = new();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);

        await ResolveAndRespondAsync(context, pipeline, options, buffer.ToArray(), clientId);
    }

    /// <summary>
    ///     This method does the actual work shared by both forms: enqueuing the decoded query
    ///     onto the shared pipeline, and writing whatever it answers with back as the HTTP
    ///     response body.
    /// </summary>
    private static async Task ResolveAndRespondAsync(
        HttpContext context,
        DnsQueryProcessingPipeline pipeline,
        DnsOverHttpsOptions options,
        byte[] requestBytes,
        string clientId = null
    )
    {
        if (requestBytes.Length == 0 || requestBytes.Length > options.MaxMessageSizeBytes)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        TaskCompletionSource<byte[]> responseSource = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                requestBytes,
                DnsTransportProtocolEnum.Https,
                context.Connection.RemoteIpAddress ?? IPAddress.Loopback,
                (responseBytes, _) =>
                {
                    responseSource.TrySetResult(responseBytes.ToArray());
                    return ValueTask.CompletedTask;
                },
                clientId
            ),
            context.RequestAborted
        );

        byte[] responseBytes = await responseSource.Task.WaitAsync(context.RequestAborted);

        context.Response.ContentType = DnsMessageContentType;
        await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted);
    }
}

using System.Buffers.Text;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class handles DNS-over-HTTPS requests (RFC 8484 §4.1): the GET form (the query
///     base64url-encoded in a <c>dns</c> query-string parameter) and the POST form (the
///     wire-format message as the body). <see cref="DnsOverHttpsHostedService" /> and
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension" /> both use it, so the
///     two hosting modes answer the same way. Each query goes to
///     <see cref="DnsQueryProcessingPipeline" /> (see that class) with the connection's remote
///     address, which the web host's forwarded-headers handling may have set from a trusted
///     proxy.
///     <para>
///         The pipeline either answers a query or reports why it dropped it
///         (<see cref="DnsQueryDropReasonEnum" />), and the handler turns the report into an HTTP
///         outcome: <c>429</c> when rate limited, <c>400</c> for a malformed message, <c>500</c> for
///         an internal failure or an oversized reply, and for a denied client no response at all,
///         because the connection is aborted (the same silence a UDP client gets, so a blocked
///         source learns nothing). If the pipeline reports nothing within
///         <see cref="ResponseBackstop" />, the request gets <c>504</c>, so no request waits for
///         a pipeline that has stalled.
///     </para>
/// </summary>
internal static class DnsOverHttpsRequestHandler
{
    /// <summary>
    ///     This constant contains the media type RFC 8484 §6 registers for a wire-format DNS
    ///     message over HTTP, required on a POST body and set on every response. A POST whose
    ///     Content-Type differs in any way, parameters included, gets <c>415</c>.
    /// </summary>
    private const string DnsMessageContentType = "application/dns-message";

    /// <summary>
    ///     This field contains the longest the handler waits for the pipeline to answer or report
    ///     a drop. Normal queries finish within the recursive resolver's overall timeout, so
    ///     reaching this means the pipeline is stalled, and the client gets <c>504</c>.
    /// </summary>
    internal static readonly TimeSpan ResponseBackstop = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This method answers the GET form of RFC 8484 §4.1. A missing or undecodable
    ///     <c>dns</c> parameter gets <c>400</c>.
    /// </summary>
    /// <param name="context">The current HTTP request/response.</param>
    /// <param name="pipeline">The pipeline the decoded query is enqueued onto.</param>
    /// <param name="options">The maximum message size.</param>
    /// <param name="gate">
    ///     The start/stop gate (see <see cref="DnsOverHttpsTransportGate" />), or
    ///     <see langword="null" />. <see cref="DnsOverHttpsHostedService" /> passes none, since it
    ///     stops its own Kestrel server; the mapping made by
    ///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />
    ///     passes one. While the gate is closed the request gets <c>503</c>.
    /// </param>
    /// <param name="clientId">
    ///     The <c>{clientid}</c> path segment of the request, or <see langword="null" /> for the
    ///     bare route (see <see cref="Server.Core.Pipeline.DnsInboundMessage.ClientId" /> for how it
    ///     is used).
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
    ///     This method answers the POST form of RFC 8484 §4.1.
    /// </summary>
    /// <param name="context">The current HTTP request/response.</param>
    /// <param name="pipeline">The pipeline the decoded query is enqueued onto.</param>
    /// <param name="options">The maximum message size.</param>
    /// <param name="gate">The start/stop gate, or <see langword="null" /> (see <see cref="HandleGetAsync" />).</param>
    /// <param name="clientId">The <c>{clientid}</c> path segment, or <see langword="null" /> (see <see cref="HandleGetAsync" />).</param>
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

        // Cap this request's body (not the whole host's default) just above
        // MaxMessageSizeBytes, so a client cannot make the handler buffer a huge body before the
        // size check in ResolveAndRespondAsync rejects it.
        context.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize =
            options.MaxMessageSizeBytes + 1024;

        using MemoryStream buffer = new();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);

        await ResolveAndRespondAsync(context, pipeline, options, buffer.ToArray(), clientId);
    }

    /// <summary>
    ///     This method does the work shared by both forms: it rejects an empty or oversized
    ///     message with <c>400</c>, enqueues the query onto the pipeline, and writes the answer
    ///     back as the response body, or maps a reported drop to a status (see the class summary).
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

        // Completed once by either the responder or the drop notifier; the pipeline calls
        // exactly one of them.
        TaskCompletionSource<(byte[] Response, DnsQueryDropReasonEnum? DropReason)> outcomeSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                requestBytes,
                DnsTransportProtocolEnum.Https,
                context.Connection.RemoteIpAddress ?? IPAddress.Loopback,
                (responseBytes, _) =>
                {
                    outcomeSource.TrySetResult((responseBytes.ToArray(), null));
                    return ValueTask.CompletedTask;
                },
                clientId
            )
            {
                OnDropped = reason => outcomeSource.TrySetResult((null, reason)),
            },
            context.RequestAborted
        );

        (byte[] Response, DnsQueryDropReasonEnum? DropReason) outcome;

        try
        {
            outcome = await outcomeSource.Task.WaitAsync(ResponseBackstop, context.RequestAborted);
        }
        catch (TimeoutException)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }

        if (outcome.DropReason is { } dropReason)
        {
            if (dropReason == DnsQueryDropReasonEnum.ClientDenied)
            {
                // No reply at all, like a denied UDP client.
                context.Abort();
                return;
            }

            context.Response.StatusCode = dropReason switch
            {
                DnsQueryDropReasonEnum.RateLimited or DnsQueryDropReasonEnum.SubnetRateLimited =>
                    StatusCodes.Status429TooManyRequests,
                DnsQueryDropReasonEnum.Malformed => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError,
            };
            return;
        }

        context.Response.ContentType = DnsMessageContentType;
        await context.Response.Body.WriteAsync(outcome.Response, context.RequestAborted);
    }
}

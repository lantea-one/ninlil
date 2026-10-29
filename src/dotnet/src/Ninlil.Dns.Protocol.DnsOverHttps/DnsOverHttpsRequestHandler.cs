using System.Buffers.Text;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
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
    ///     message over HTTP, required on a POST body and set on every response. A POST matches
    ///     it case-insensitively and whatever the parameters (RFC 9110 §8.3.1, for example
    ///     <c>; charset=utf-8</c>); any other media type, or none, gets <c>415</c>.
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

        if (!IsDnsMessageContentType(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        // Cap this request's body (not the whole host's default) just above
        // MaxMessageSizeBytes, so a client cannot make the handler buffer a huge body before the
        // size check in ResolveAndRespondAsync rejects it.
        context.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize =
            options.MaxMessageSizeBytes + 1024;

        byte[] requestBytes = await ReadBodyAsync(context, options);

        if (requestBytes is null)
            return;

        await ResolveAndRespondAsync(context, pipeline, options, requestBytes, clientId);
    }

    /// <summary>
    ///     This method reads the POST body into a new array that nothing else references. The
    ///     pipeline keeps the array for the life of the query (see
    ///     <see cref="DnsInboundMessage.RequestBytes" />), so it must be private to this request.
    ///     When the request declares a length that the size limit allows, the body is read straight
    ///     into an array of exactly that length, which is the only copy; otherwise (no length, as
    ///     with chunked bodies, or a length over the limit that the caller must still answer with
    ///     <c>400</c>) it is buffered and copied out once.
    /// </summary>
    /// <returns>
    ///     The body, or <see langword="null" /> after this method answered <c>400</c> because the
    ///     body ended before the declared length.
    /// </returns>
    private static async Task<byte[]> ReadBodyAsync(
        HttpContext context,
        DnsOverHttpsOptions options
    )
    {
        long? declaredLength = context.Request.ContentLength;

        if (declaredLength is > 0 && declaredLength <= options.MaxMessageSizeBytes)
        {
            byte[] exact = new byte[declaredLength.Value];

            try
            {
                await context.Request.Body.ReadExactlyAsync(exact, context.RequestAborted);
            }
            catch (EndOfStreamException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return null;
            }

            return exact;
        }

        using MemoryStream buffer = new();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        return buffer.ToArray();
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
                ClientAddressOf(context),
                (responseBytes, _) =>
                {
                    outcomeSource.TrySetResult((WholeArrayOrCopy(responseBytes), null));
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
        context.Response.Headers[HeaderNames.CacheControl] = CacheControlFor(outcome.Response);
        await context.Response.Body.WriteAsync(outcome.Response, context.RequestAborted);
    }

    /// <summary>
    ///     This method returns the array behind an encoded reply when the memory covers all of it,
    ///     and a copy otherwise. The pipeline hands each reply to the responder once and never
    ///     touches it again, and the request task reads it only after the responder returned, so
    ///     the array needs no copy of its own. A slice or memory without an array is copied,
    ///     because the owner of that memory may reuse it.
    /// </summary>
    private static byte[] WholeArrayOrCopy(ReadOnlyMemory<byte> responseBytes) =>
        MemoryMarshal.TryGetArray(responseBytes, out ArraySegment<byte> segment)
        && segment.Offset == 0
        && segment.Count == segment.Array.Length
            ? segment.Array
            : responseBytes.ToArray();

    /// <summary>
    ///     This method returns the address the pipeline sees as the client: the connection's
    ///     remote address (which the forwarded-headers handling may have set from a trusted
    ///     proxy), or the unspecified address <c>::</c> when the connection has none. The
    ///     fallback is not loopback, because access rules and grants may trust loopback.
    /// </summary>
    /// <param name="context">The current HTTP request.</param>
    internal static IPAddress ClientAddressOf(HttpContext context) =>
        context.Connection.RemoteIpAddress ?? IPAddress.IPv6None;

    /// <summary>
    ///     This method tells whether a Content-Type header names <c>application/dns-message</c>:
    ///     the media type is compared case-insensitively and its parameters are ignored
    ///     (RFC 9110 §8.3.1). A missing or unparsable header does not match.
    /// </summary>
    private static bool IsDnsMessageContentType(string contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue parsed)
        && parsed.MediaType.Equals(DnsMessageContentType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     This method builds the <c>Cache-Control</c> value for a reply (RFC 8484 §5.1). The
    ///     freshness lifetime is the smallest TTL in the Answer section; with no answer records
    ///     and an SOA in the Authority section it is the smaller of the SOA's TTL and its
    ///     MINIMUM field (RFC 2308 §5); otherwise (SERVFAIL, REFUSED, an empty reply without an
    ///     SOA, bytes that do not decode) it is 0. The value is <c>private</c> because an answer
    ///     depends on who asks (filters, rewrites, schedules, access rules, the
    ///     <c>{clientid}</c> path), so a shared HTTP cache must not hand it to another client.
    /// </summary>
    /// <param name="response">The wire-format reply.</param>
    private static string CacheControlFor(byte[] response)
    {
        uint maxAge = 0;

        try
        {
            DnsMessage message = DnsMessageReader.Read(response);

            // Only NOERROR and NXDOMAIN replies are worth caching; SERVFAIL, REFUSED and the
            // rest are errors that must be asked again.
            bool cacheable =
                message.ResponseCode is DnsResponseCodeEnum.NoError or DnsResponseCodeEnum.NxDomain;

            if (cacheable && message.Answers.Count > 0)
            {
                maxAge = message.Answers.Min(record => record.TimeToLive);
            }
            else if (cacheable)
            {
                DnsResourceRecord soa = message.AuthorityRecords.FirstOrDefault(record =>
                    record.Type == DnsResourceRecordTypeEnum.Soa && record.Data.Length >= 20
                );

                if (soa is not null)
                {
                    // MINIMUM is the last of the five 32-bit fields that end the SOA RDATA
                    // (RFC 1035 §3.3.13), so it is the final four bytes whatever the names' length.
                    uint minimum = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                        soa.Data.AsSpan(soa.Data.Length - 4)
                    );
                    maxAge = Math.Min(soa.TimeToLive, minimum);
                }
            }
        }
        catch (Exception)
        {
            // Undecodable bytes are still sent, but nothing may cache them.
            maxAge = 0;
        }

        return $"private, max-age={maxAge}";
    }
}

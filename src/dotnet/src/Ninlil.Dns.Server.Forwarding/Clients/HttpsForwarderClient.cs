using System.Net.Http.Headers;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class forwards a query over DNS-over-HTTPS (RFC 8484) as a POST: the wire-format
///     message is the request body, with <c>Content-Type: application/dns-message</c>. A
///     non-success HTTP status throws. Certificates are validated by the default
///     <see cref="HttpClient" /> handler.
///     <para>
///         The query is sent with message ID 0: RFC 8484 §4.1 says DoH clients SHOULD use a DNS ID
///         of 0 in every request so HTTP caches see equivalent queries as equal, and HTTP itself
///         pairs the request with its response. A reply for another question than the one sent
///         throws an <see cref="InvalidDataException" />. The reply handed back carries the ID of
///         the query the caller passed in.
///     </para>
/// </summary>
public sealed class HttpsForwarderClient : IUpstreamForwarderClient
{
    /// <summary>
    ///     This constant contains RFC 8484 §6's required media type for a wire-format DNS
    ///     message carried over HTTP.
    /// </summary>
    private const string DnsMessageContentType = "application/dns-message";

    /// <summary>
    ///     This field contains the one <see cref="HttpClient" /> every forwarded query reuses, so
    ///     connections to a forwarder are pooled and kept alive.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new();

    private readonly HttpClient httpClient;

    /// <summary>
    ///     This constructor builds a client that uses the shared <see cref="HttpClient" />.
    /// </summary>
    public HttpsForwarderClient()
    {
        httpClient = SharedHttpClient;
    }

    /// <summary>
    ///     This constructor lets tests answer the HTTP request with a fake handler.
    /// </summary>
    /// <param name="handler">
    ///     The handler that receives the request. The client never disposes it.
    /// </param>
    internal HttpsForwarderClient(HttpMessageHandler handler)
    {
        httpClient = new HttpClient(handler, disposeHandler: false);
    }

    /// <inheritdoc />
    public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Https;

    /// <inheritdoc />
    public async Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    )
    {
        // RFC 8484 §4.1: ID 0 keeps equivalent queries identical for HTTP caches.
        DnsMessage sent = ForwarderQueryMessage.WithId(query, 0);
        byte[] queryBytes = DnsMessageWriter.Write(sent);

        using HttpRequestMessage request = new(HttpMethod.Post, definition.DohUri)
        {
            Content = new ByteArrayContent(queryBytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(DnsMessageContentType);

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            stoppingToken
        );

        response.EnsureSuccessStatusCode();

        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(stoppingToken);

        DnsMessage reply = DnsMessageReader.Read(responseBytes);

        if (!ForwarderQueryMessage.IsReplyTo(sent, reply))
            throw new InvalidDataException(
                "The forwarder's DNS-over-HTTPS response answers another question than the query."
            );

        reply.Id = query.Id;

        return reply;
    }
}

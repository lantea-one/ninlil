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

    /// <inheritdoc />
    public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Https;

    /// <inheritdoc />
    public async Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    )
    {
        byte[] queryBytes = DnsMessageWriter.Write(query);

        using HttpRequestMessage request = new(HttpMethod.Post, definition.DohUri)
        {
            Content = new ByteArrayContent(queryBytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(DnsMessageContentType);

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            stoppingToken
        );

        response.EnsureSuccessStatusCode();

        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(stoppingToken);

        return DnsMessageReader.Read(responseBytes);
    }
}

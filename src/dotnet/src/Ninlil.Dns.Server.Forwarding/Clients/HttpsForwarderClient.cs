using System.Net.Http.Headers;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class forwards a query per RFC 8484's DNS-over-HTTPS POST form: the raw wire-format
///     message as the request body, <c>Content-Type: application/dns-message</c> -- the
///     outbound counterpart of what this project's own <c>DnsOverHttpsHostedService.HandlePostAsync</c>
///     already expects on the server side.
/// </summary>
public sealed class HttpsForwarderClient : IUpstreamForwarderClient
{
    /// <summary>
    ///     This constant contains RFC 8484 §6's required media type for a wire-format DNS
    ///     message carried over HTTP.
    /// </summary>
    private const string DnsMessageContentType = "application/dns-message";

    /// <summary>
    ///     This property contains the single, shared <see cref="HttpClient" /> every forwarded
    ///     query reuses -- creating a fresh one per call would defeat connection pooling/keep-
    ///     alive against a forwarder this server may query many times a second.
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

using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class forwards a query over DNS-over-QUIC (RFC 9250): a new QUIC connection per
///     query, one bidirectional stream, 2-byte length-prefixed framing. The query is sent with
///     message ID 0, as RFC 9250 §4.2.1 requires (a copy of the caller's message, built by
///     <see cref="ForwarderQueryMessage.WithId" />). A reply is accepted only when its ID is 0
///     and it repeats the question that was sent; anything else is a failed attempt
///     (<see cref="InvalidDataException" />). The caller's own ID is restored on the reply. The
///     certificate is validated against the operating system's
///     trust store (never disabled), for <see cref="UpstreamForwarderDefinition.TlsHostName" />
///     when named or for the forwarder's IP address otherwise. It needs libmsquic, hence the
///     platform attributes.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class QuicForwarderClient : IUpstreamForwarderClient
{
    /// <summary>
    ///     This field contains the registered DNS-over-QUIC ALPN token (RFC 9250 §4.1). The
    ///     listener in <c>Ninlil.Dns.Protocol.DnsOverQuic</c> keeps a private copy of it.
    /// </summary>
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    /// <summary>
    ///     This field contains the message ID of every DNS-over-QUIC message (RFC 9250 §4.2.1).
    /// </summary>
    private const ushort QuicMessageId = 0;

    /// <summary>
    ///     This field contains the certificate check used instead of the operating system's trust
    ///     store, or <see langword="null" /> for the default check. Only tests set it.
    /// </summary>
    private readonly RemoteCertificateValidationCallback certificateValidation;

    /// <summary>
    ///     This constructor creates a client that validates the certificate against the operating
    ///     system's trust store.
    /// </summary>
    public QuicForwarderClient() { }

    /// <summary>
    ///     This constructor creates a client with its own certificate check, so a test can talk
    ///     to a loopback listener that has a self-signed certificate. Production code never
    ///     passes a callback that accepts everything.
    /// </summary>
    /// <param name="certificateValidation">The certificate check to use.</param>
    internal QuicForwarderClient(RemoteCertificateValidationCallback certificateValidation) =>
        this.certificateValidation = certificateValidation;

    /// <inheritdoc />
    public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Quic;

    /// <inheritdoc />
    public async Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    )
    {
        if (!QuicConnection.IsSupported)
            throw new PlatformNotSupportedException(
                "DNS-over-QUIC forwarding requires libmsquic (and, on Linux, OpenSSL) to be installed."
            );

        string targetHost = definition.TlsHostName ?? definition.Address.ToString();

        await using QuicConnection connection = await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                RemoteEndPoint = new IPEndPoint(definition.Address, definition.Port),
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = targetHost,
                    ApplicationProtocols = [DoqApplicationProtocol],
                    RemoteCertificateValidationCallback = certificateValidation,
                },
            },
            stoppingToken
        );

        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional,
            stoppingToken
        );

        // A copy with ID 0: the caller's message keeps its own ID.
        DnsMessage outbound = ForwarderQueryMessage.WithId(query, QuicMessageId);
        byte[] queryBytes = DnsMessageWriter.Write(outbound);
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);

        await stream.WriteAsync(lengthPrefix, stoppingToken);
        await stream.WriteAsync(queryBytes, completeWrites: true, stoppingToken);

        await ReadExactAsync(stream, lengthPrefix, stoppingToken);
        ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        byte[] responseBuffer = new byte[responseLength];
        await ReadExactAsync(stream, responseBuffer, stoppingToken);

        DnsMessage reply = DnsMessageReader.Read(responseBuffer);

        // RFC 9250 §4.2.1: a DoQ message has ID 0. A reply with another ID, or for another
        // question, is not an answer to this query.
        if (reply.Id != QuicMessageId || !ForwarderQueryMessage.IsReplyTo(outbound, reply))
            throw new InvalidDataException(
                "The DNS-over-QUIC reply does not answer the question that was sent, or its message ID is not 0."
            );

        reply.Id = query.Id;
        return reply;
    }

    /// <summary>
    ///     This method fills a buffer completely from a stream, throwing if the stream ends
    ///     before every requested byte arrives.
    /// </summary>
    private static async Task ReadExactAsync(
        QuicStream stream,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
                throw new IOException(
                    "The forwarder closed the stream before a complete response arrived."
                );

            totalRead += read;
        }
    }
}

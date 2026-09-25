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
///     its existing message ID. The certificate is validated against the operating system's
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
    ///     This field contains the registered DNS-over-QUIC ALPN token (RFC 9250 §7.1). The
    ///     listener in <c>Ninlil.Dns.Protocol.DnsOverQuic</c> keeps a private copy of it.
    /// </summary>
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

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
                },
            },
            stoppingToken
        );

        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional,
            stoppingToken
        );

        byte[] queryBytes = DnsMessageWriter.Write(query);
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);

        await stream.WriteAsync(lengthPrefix, stoppingToken);
        await stream.WriteAsync(queryBytes, completeWrites: true, stoppingToken);

        await ReadExactAsync(stream, lengthPrefix, stoppingToken);
        ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        byte[] responseBuffer = new byte[responseLength];
        await ReadExactAsync(stream, responseBuffer, stoppingToken);

        return DnsMessageReader.Read(responseBuffer);
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

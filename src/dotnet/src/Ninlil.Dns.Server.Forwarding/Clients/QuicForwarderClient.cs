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
///     This class forwards a query per RFC 9250's DNS-over-QUIC transport: one dedicated
///     bidirectional stream per query, 2-byte length-prefixed framing, matching this project's
///     own DoQ <em>listener</em> shape but as the client/connecting side. Certificate validation
///     uses .NET's ordinary OS trust-store validation (never disabled), against
///     <see cref="UpstreamForwarderDefinition.TlsHostName" /> when named, or the forwarder's own
///     IP address string otherwise.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class QuicForwarderClient : IUpstreamForwarderClient
{
    /// <summary>
    ///     This constant contains RFC 9250 §7.1's registered ALPN token for DNS-over-QUIC --
    ///     redeclared here rather than referenced across projects, since
    ///     <c>Ninlil.Dns.Protocol.DnsOverQuic.DnsOverQuicListener</c>'s own copy is private
    ///     to that listener.
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

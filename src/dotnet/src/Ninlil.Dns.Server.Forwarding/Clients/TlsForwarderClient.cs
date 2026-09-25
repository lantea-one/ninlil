using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class forwards a query over DNS-over-TLS (RFC 7858): a new TCP and TLS connection per
///     query, with the same 2-byte length-prefixed framing as plain TCP. The forwarder's
///     certificate is validated against the operating system's trust store, for
///     <see cref="UpstreamForwarderDefinition.TlsHostName" /> when the definition names a host,
///     or for the forwarder's IP address otherwise. Validation is never disabled.
/// </summary>
public sealed class TlsForwarderClient : IUpstreamForwarderClient
{
    /// <inheritdoc />
    public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Tls;

    /// <inheritdoc />
    public async Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    )
    {
        IPEndPoint endpoint = new(definition.Address, definition.Port);
        string targetHost = definition.TlsHostName ?? definition.Address.ToString();

        using TcpClient tcpClient = new(endpoint.AddressFamily);
        await tcpClient.ConnectAsync(endpoint.Address, endpoint.Port, stoppingToken);

        await using SslStream sslStream = new(tcpClient.GetStream(), leaveInnerStreamOpen: false);

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = targetHost },
            stoppingToken
        );

        byte[] queryBytes = DnsMessageWriter.Write(query);
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);

        await sslStream.WriteAsync(lengthPrefix, stoppingToken);
        await sslStream.WriteAsync(queryBytes, stoppingToken);

        await ReadExactAsync(sslStream, lengthPrefix, stoppingToken);
        ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        byte[] responseBuffer = new byte[responseLength];
        await ReadExactAsync(sslStream, responseBuffer, stoppingToken);

        return DnsMessageReader.Read(responseBuffer);
    }

    /// <summary>
    ///     This method fills a buffer completely from a stream, throwing if the connection closes
    ///     before every requested byte arrives.
    /// </summary>
    private static async Task ReadExactAsync(
        Stream stream,
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
                    "The forwarder closed the connection before a complete response arrived."
                );

            totalRead += read;
        }
    }
}

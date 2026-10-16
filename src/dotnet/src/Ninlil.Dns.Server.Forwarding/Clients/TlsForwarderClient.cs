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
///     <para>
///         The query goes out with a new random message ID (RFC 5452 §9.2) and the reply must
///         carry that ID and repeat the question; otherwise the attempt fails with an
///         <see cref="InvalidDataException" />. The reply handed back carries the ID of the query
///         the caller passed in.
///     </para>
/// </summary>
public sealed class TlsForwarderClient : IUpstreamForwarderClient
{
    private readonly Func<ushort> newMessageId;

    /// <summary>
    ///     This constructor builds a client that draws its message IDs from the cryptographic
    ///     random source.
    /// </summary>
    public TlsForwarderClient()
        : this(null) { }

    /// <summary>
    ///     This constructor lets tests fix the message ID.
    /// </summary>
    /// <param name="newMessageId">
    ///     Returns the ID of each attempt; <see langword="null" /> uses
    ///     <see cref="ForwarderQueryMessage.NewRandomId" />.
    /// </param>
    internal TlsForwarderClient(Func<ushort> newMessageId)
    {
        this.newMessageId = newMessageId ?? ForwarderQueryMessage.NewRandomId;
    }

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

        DnsMessage sent = ForwarderQueryMessage.WithId(query, newMessageId());
        byte[] queryBytes = DnsMessageWriter.Write(sent);
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);

        await sslStream.WriteAsync(lengthPrefix, stoppingToken);
        await sslStream.WriteAsync(queryBytes, stoppingToken);

        await ReadExactAsync(sslStream, lengthPrefix, stoppingToken);
        ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        byte[] responseBuffer = new byte[responseLength];
        await ReadExactAsync(sslStream, responseBuffer, stoppingToken);

        DnsMessage response = DnsMessageReader.Read(responseBuffer);

        if (response.Id != sent.Id || !ForwarderQueryMessage.IsReplyTo(sent, response))
            throw new InvalidDataException(
                "The forwarder's TLS response does not match the query that was sent."
            );

        response.Id = query.Id;

        return response;
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

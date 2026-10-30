using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class forwards a query over plain DNS (RFC 1035): one UDP datagram out, one back,
///     and a single TCP retry when the UDP answer is truncated (RFC 1035 §4.2.1 defines the TC
///     bit; RFC 1123 §6.1.3.2 and RFC 7766 §5 send the requester to TCP), as a stub resolver
///     does.
///     <para>
///         Every attempt (the UDP datagram and the TCP retry alike) sends a copy of the query with
///         a new random message ID, so the ID on the wire is unpredictable to an off-path forger
///         (RFC 5452 §9.2) and the client's own ID never leaves this server. A UDP datagram whose
///         ID or question does not match is dropped and the wait goes on until the caller's
///         per-forwarder timeout; a TCP reply that does not match is a failure, since a connected
///         stream has no stray datagrams to skip. The reply handed back carries the ID of the
///         query the caller passed in.
///     </para>
/// </summary>
public sealed class PlainForwarderClient : IUpstreamForwarderClient
{
    private readonly IOutboundUdpSocketPool socketPool;
    private readonly ILogger<PlainForwarderClient> logger;
    private readonly Func<ushort> newMessageId;

    /// <summary>
    ///     This constructor builds a client that draws its message IDs from the cryptographic
    ///     random source.
    /// </summary>
    /// <param name="socketPool">
    ///     The shared outbound UDP socket pool (see <see cref="IOutboundUdpSocketPool" />).
    ///     <see langword="null" /> (the default, for code that builds this client directly) uses a
    ///     new socket on an OS-chosen port per forward.
    /// </param>
    /// <param name="logger">
    ///     The logger this client records dropped (spoofed, stray or malformed) responses to.
    ///     <see langword="null" /> (the default) drops them without logging.
    /// </param>
    public PlainForwarderClient(
        IOutboundUdpSocketPool socketPool = null,
        ILogger<PlainForwarderClient> logger = null
    )
        : this(socketPool, logger, null) { }

    /// <summary>
    ///     This constructor lets tests fix the message IDs.
    /// </summary>
    /// <param name="socketPool">See the public constructor.</param>
    /// <param name="logger">See the public constructor.</param>
    /// <param name="newMessageId">
    ///     Returns the ID of each attempt; <see langword="null" /> uses
    ///     <see cref="ForwarderQueryMessage.NewRandomId" />.
    /// </param>
    internal PlainForwarderClient(
        IOutboundUdpSocketPool socketPool,
        ILogger<PlainForwarderClient> logger,
        Func<ushort> newMessageId
    )
    {
        this.socketPool = socketPool;
        this.logger = logger;
        this.newMessageId = newMessageId ?? ForwarderQueryMessage.NewRandomId;
    }

    /// <summary>
    ///     This constant contains the largest UDP datagram this client reads, the same as the
    ///     server-side buffer of <c>Ninlil.Dns.Protocol.Plain.UdpDnsListener</c>.
    /// </summary>
    private const int MaximumDatagramSize = 65527;

    /// <inheritdoc />
    public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Plain;

    /// <inheritdoc />
    public async Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    )
    {
        IPEndPoint endpoint = new(definition.Address, definition.Port);

        DnsMessage udpQuery = ForwarderQueryMessage.WithId(query, newMessageId());
        DnsMessage response = await ForwardUdpAsync(udpQuery, endpoint, stoppingToken);

        if (response.IsTruncated)
        {
            // The retry is a new transaction, so it gets its own ID.
            DnsMessage tcpQuery = ForwarderQueryMessage.WithId(query, newMessageId());
            response = await ForwardTcpAsync(tcpQuery, endpoint, stoppingToken);
        }

        response.Id = query.Id;

        return response;
    }

    /// <summary>
    ///     This method sends the query as one UDP datagram and returns the first response that
    ///     comes from <paramref name="endpoint" />, parses, carries the ID of
    ///     <paramref name="query" /> and repeats its question (RFC 5452 §9.1). Anything else is
    ///     logged and discarded, since a pooled socket can receive stray or spoofed datagrams.
    ///     Only cancellation of <paramref name="stoppingToken" /> (usually the caller's
    ///     per-forwarder timeout) ends a wait that sees no acceptable datagram; the method never
    ///     returns <see langword="null" />.
    /// </summary>
    private async Task<DnsMessage> ForwardUdpAsync(
        DnsMessage query,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        byte[] queryBytes = DnsMessageWriter.Write(query);
        ushort expectedId = query.Id;

        if (socketPool is null)
            return await ForwardUdpWithFreshSocketAsync(query, queryBytes, endpoint, stoppingToken);

        using OutboundUdpSocketLease lease = await socketPool.RentAsync(
            endpoint.AddressFamily,
            stoppingToken
        );

        try
        {
            await lease.Socket.SendToAsync(queryBytes, endpoint, stoppingToken);

            byte[] receiveBuffer = new byte[MaximumDatagramSize];

            while (true)
            {
                SocketReceiveFromResult result = await lease.Socket.ReceiveFromAsync(
                    receiveBuffer,
                    endpoint,
                    stoppingToken
                );

                if (!Equals(result.RemoteEndPoint, endpoint))
                {
                    logger?.LogDebug(
                        "Discarding a UDP response to a forwarded query: it arrived from "
                            + "{ActualEndpoint}, not the queried forwarder {ExpectedEndpoint}.",
                        result.RemoteEndPoint,
                        endpoint
                    );
                    continue;
                }

                DnsMessage response;

                try
                {
                    response = DnsMessageReader.Read(receiveBuffer.AsSpan(0, result.ReceivedBytes));
                }
                catch (FormatException)
                {
                    logger?.LogDebug(
                        "Discarding a malformed UDP response to a forwarded query from {Endpoint}.",
                        endpoint
                    );
                    continue;
                }

                if (response.Id != expectedId)
                {
                    logger?.LogDebug(
                        "Discarding a UDP response to a forwarded query: transaction ID "
                            + "{ActualId} did not match the expected {ExpectedId}.",
                        response.Id,
                        expectedId
                    );
                    continue;
                }

                if (!ForwarderQueryMessage.IsReplyTo(query, response))
                {
                    logger?.LogDebug(
                        "Discarding a UDP response to a forwarded query from {Endpoint}: its "
                            + "question is not the one that was sent.",
                        endpoint
                    );
                    continue;
                }

                return response;
            }
        }
        catch (SocketException)
        {
            lease.MarkUnhealthy();
            throw;
        }
    }

    /// <summary>
    ///     This method forwards over a new socket on an OS-chosen port, for when no
    ///     <see cref="IOutboundUdpSocketPool" /> was supplied. (With the pool,
    ///     <see cref="IOutboundUdpSocketPool.RentAsync" /> already hands out such a socket when
    ///     <c>OutboundQueryOptions.EnableSourcePortPool</c> is off.) It applies the same source and
    ///     transaction-ID and question checks as <see cref="ForwardUdpAsync" />, without logging.
    /// </summary>
    private static async Task<DnsMessage> ForwardUdpWithFreshSocketAsync(
        DnsMessage query,
        byte[] queryBytes,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        using Socket socket = new(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        await socket.SendToAsync(queryBytes, endpoint, stoppingToken);

        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (true)
        {
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                receiveBuffer,
                endpoint,
                stoppingToken
            );

            if (!Equals(result.RemoteEndPoint, endpoint))
                continue;

            DnsMessage response;

            try
            {
                response = DnsMessageReader.Read(receiveBuffer.AsSpan(0, result.ReceivedBytes));
            }
            catch (FormatException)
            {
                continue;
            }

            if (response.Id != query.Id || !ForwarderQueryMessage.IsReplyTo(query, response))
                continue;

            return response;
        }
    }

    /// <summary>
    ///     This method sends the query over a new, single-use TCP connection with the 2-byte
    ///     length prefix of RFC 1035 §4.2.2 and reads back one length-prefixed response. A
    ///     connected TCP socket only hears from its peer, so no source check is needed here.
    /// </summary>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the response carries another ID or another question than
    ///     <paramref name="query" />: a stream has no stray datagrams to skip, so the attempt
    ///     fails.
    /// </exception>
    private static async Task<DnsMessage> ForwardTcpAsync(
        DnsMessage query,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        byte[] queryBytes = DnsMessageWriter.Write(query);
        using Socket socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(endpoint, stoppingToken);

        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)queryBytes.Length);
        await socket.SendAsync(lengthPrefix, stoppingToken);
        await socket.SendAsync(queryBytes, stoppingToken);

        await ReadExactAsync(socket, lengthPrefix, stoppingToken);
        ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        byte[] responseBuffer = new byte[responseLength];
        await ReadExactAsync(socket, responseBuffer, stoppingToken);

        DnsMessage response = DnsMessageReader.Read(responseBuffer);

        if (response.Id != query.Id || !ForwarderQueryMessage.IsReplyTo(query, response))
            throw new InvalidDataException(
                "The forwarder's TCP response does not match the query that was sent."
            );

        return response;
    }

    /// <summary>
    ///     This method fills a buffer completely from a socket, throwing if the connection closes
    ///     before every requested byte arrives.
    /// </summary>
    private static async Task ReadExactAsync(
        Socket socket,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await socket.ReceiveAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
                throw new IOException(
                    "The forwarder closed the connection before a complete response arrived."
                );

            totalRead += read;
        }
    }
}

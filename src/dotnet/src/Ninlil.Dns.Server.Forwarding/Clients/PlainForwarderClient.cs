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
///     and a single TCP retry when the UDP answer is truncated (RFC 1035 §4.2.1), as a stub
///     resolver does.
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
public sealed class PlainForwarderClient(
    IOutboundUdpSocketPool socketPool = null,
    ILogger<PlainForwarderClient> logger = null
) : IUpstreamForwarderClient
{
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
        byte[] queryBytes = DnsMessageWriter.Write(query);
        IPEndPoint endpoint = new(definition.Address, definition.Port);

        DnsMessage response = await ForwardUdpAsync(queryBytes, query.Id, endpoint, stoppingToken);

        if (response.IsTruncated)
            response = await ForwardTcpAsync(queryBytes, endpoint, stoppingToken);

        return response;
    }

    /// <summary>
    ///     This method sends the query as one UDP datagram and returns the first response that
    ///     comes from <paramref name="endpoint" />, parses, and carries
    ///     <paramref name="expectedId" />. Anything else is logged and discarded, since a pooled
    ///     socket can receive stray or spoofed datagrams. Only cancellation of
    ///     <paramref name="stoppingToken" /> (usually the caller's per-forwarder timeout) ends a
    ///     wait that sees no acceptable datagram; the method never returns
    ///     <see langword="null" />. The question section is not compared.
    /// </summary>
    private async Task<DnsMessage> ForwardUdpAsync(
        byte[] queryBytes,
        ushort expectedId,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        if (socketPool is null)
            return await ForwardUdpWithFreshSocketAsync(
                queryBytes,
                expectedId,
                endpoint,
                stoppingToken
            );

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
    ///     transaction-ID checks as <see cref="ForwardUdpAsync" />, without logging.
    /// </summary>
    private static async Task<DnsMessage> ForwardUdpWithFreshSocketAsync(
        byte[] queryBytes,
        ushort expectedId,
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

            if (response.Id != expectedId)
                continue;

            return response;
        }
    }

    /// <summary>
    ///     This method sends the query over a new, single-use TCP connection with the 2-byte
    ///     length prefix of RFC 1035 §4.2.2 and reads back one length-prefixed response. A
    ///     connected TCP socket only hears from its peer, so no source check is needed here.
    /// </summary>
    private static async Task<DnsMessage> ForwardTcpAsync(
        byte[] queryBytes,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
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

        return DnsMessageReader.Read(responseBuffer);
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

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
///     This class forwards a query per RFC 1035's original plain UDP/TCP transport: one
///     datagram out, one datagram back, falling back to a single TCP retry when the forwarder's
///     own UDP answer comes back truncated (RFC 1035 §4.2.1) -- the same client-side behavior
///     every real stub resolver implements, mirrored here for this project's own outbound
///     forwarding client role.
/// </summary>
/// <param name="socketPool">
///     The shared, source-port-pooling outbound socket pool this client's own UDP forwards are
///     sent through -- see <see cref="IOutboundUdpSocketPool" />'s own doc comment. Defaults to
///     <see langword="null" /> so a caller/test that constructs this client directly (bypassing
///     dependency injection) keeps compiling and running unchanged, falling back to this class's
///     own pre-pool behavior: a fresh, one-off, OS-ephemeral-port socket per forward.
/// </param>
/// <param name="logger">
///     The logger this client records a dropped, mismatched (spoofed or stray) response to.
///     Defaults to <see langword="null" />, in which case a mismatch is silently dropped without
///     logging, the same graceful-degradation idiom this project's other optional constructor
///     parameters already establish.
/// </param>
public sealed class PlainForwarderClient(
    IOutboundUdpSocketPool socketPool = null,
    ILogger<PlainForwarderClient> logger = null
) : IUpstreamForwarderClient
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this client will ever read, matching
    ///     <c>Ninlil.Dns.Protocol.Plain.UdpDnsListener</c>'s own server-side buffer size.
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
    ///     This method sends the query as a single UDP datagram and reads back a single response
    ///     datagram, validating that it actually arrived from <paramref name="endpoint" /> and
    ///     carries <paramref name="expectedId" /> before accepting it -- this client's own
    ///     anti-spoofing hardening. Before this hardening existed, this method trusted the very
    ///     first datagram to arrive on this socket at all, from anyone, with no transaction-ID or
    ///     source-address check whatsoever -- a real, exploitable gap for a socket-sharing forward
    ///     path such as this one. A mismatched
    ///     datagram is logged and discarded, never accepted as this call's own answer; this
    ///     method still honors this interface's own "throw <see cref="OperationCanceledException" />
    ///     on any failure, never return <see langword="null" />" contract (<see cref="IUpstreamForwarderClient.ForwardAsync" />)
    ///     -- <paramref name="stoppingToken" />'s own cancellation (a caller-enforced per-forwarder
    ///     timeout, most commonly) is what ends a run of nothing-but-mismatched datagrams, exactly
    ///     as it would end a run of no datagrams at all.
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
    ///     This method preserves this client's own pre-pool behavior: a fresh, one-off,
    ///     OS-ephemeral-port socket per forward -- used only when <see cref="IOutboundUdpSocketPool" />
    ///     is unavailable (a caller bypassing dependency injection), since
    ///     <see cref="IOutboundUdpSocketPool.RentAsync" /> itself already returns an equivalent
    ///     one-off socket whenever <c>OutboundQueryOptions.EnableSourcePortPool</c> is off. Still
    ///     applies the same source-address/transaction-ID validation hardening
    ///     <see cref="ForwardUdpAsync" /> does with a pooled socket.
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
    ///     This method sends the query over a fresh, single-use TCP connection, 2-byte
    ///     length-prefixed per RFC 1035 §4.2.2, and reads back exactly one length-prefixed
    ///     response. A TCP connection, by construction, cannot receive a reply from anywhere but
    ///     the peer this client itself connected to -- no separate source-address check is needed
    ///     here the way <see cref="ForwardUdpAsync" /> needs one for an unconnected UDP socket.
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
    ///     before every requested byte arrives -- a forwarder's own truncated TCP stream is a
    ///     genuine failure, not something a caller can meaningfully recover from.
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

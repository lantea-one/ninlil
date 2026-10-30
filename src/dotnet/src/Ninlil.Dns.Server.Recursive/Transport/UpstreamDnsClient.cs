using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Transport;

/// <summary>
///     This class implements <see cref="IUpstreamDnsClient" /> over UDP and TCP. UDP
///     goes through the shared <see cref="IOutboundUdpSocketPool" /> when one is registered. A
///     response is accepted only from the queried address and with the query's message ID and
///     question (RFC 5452 §9.1).
/// </summary>
/// <param name="options">
///     The per-query timeout (<see cref="RecursiveServerOptions.UpstreamQueryTimeout" />), read
///     live through <see cref="IOptionsMonitor{TOptions}" />. It covers the UDP attempt and any
///     TCP retry together.
/// </param>
/// <param name="socketPool">
///     The shared outbound UDP socket pool (see <see cref="IOutboundUdpSocketPool" />).
///     <see langword="null" /> (the default, for code that builds this client directly) uses a
///     new connected socket on an OS-chosen port per query.
/// </param>
/// <param name="logger">
///     The logger this client records discarded datagrams to.
///     <see langword="null" /> (the default) drops them without logging.
/// </param>
/// <param name="serverPort">
///     The port queries go to: the IANA-assigned DNS port 53 by default. Tests point it at an
///     unprivileged port.
/// </param>
public sealed class UpstreamDnsClient(
    IOptionsMonitor<RecursiveServerOptions> options,
    IOutboundUdpSocketPool socketPool = null,
    ILogger<UpstreamDnsClient> logger = null,
    int serverPort = UpstreamDnsClient.DnsPort
) : IUpstreamDnsClient
{
    /// <summary>
    ///     This constant contains the IANA-assigned port number for DNS.
    /// </summary>
    private const int DnsPort = 53;

    /// <summary>
    ///     This constant contains the size of the UDP receive buffer, large enough for any UDP
    ///     payload.
    /// </summary>
    private const int MaximumUdpResponseSize = 65527;

    /// <inheritdoc />
    public async Task<DnsMessage> QueryAsync(
        IPAddress serverAddress,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool dnssecOk,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    )
    {
        ushort id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        DnsMessage request = new()
        {
            Id = id,
            // Iterative queries to authoritative servers do not ask for recursion.
            RecursionDesired = false,
            Questions = { new DnsQuestion(name, type, recordClass) },
            Edns = new DnsEdnsOptions(
                0,
                DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                dnssecOk,
                clientSubnetOption is null ? [] : [clientSubnetOption]
            ),
        };
        byte[] requestBytes = DnsMessageWriter.Write(request);

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken
        );
        timeoutCts.CancelAfter(options.CurrentValue.UpstreamQueryTimeout);

        DnsMessage response = await QueryUdpAsync(
            serverAddress,
            request,
            requestBytes,
            timeoutCts.Token
        );

        if (response is { IsTruncated: true })
            response =
                await QueryTcpAsync(serverAddress, requestBytes, timeoutCts.Token) ?? response;

        // A connected UDP socket or a TCP connection gets one reply; if it does not match, it is
        // stray or spoofed, and this query fails rather than waiting for the right one.
        return response is not null && IsResponseTo(response, request) ? response : null;
    }

    /// <summary>
    ///     This method checks that a response answers a request: the same message ID and the
    ///     same question (RFC 5452 §9.1). The question check stops a reply to an earlier query
    ///     with a colliding ID from being taken for this one.
    /// </summary>
    private static bool IsResponseTo(DnsMessage response, DnsMessage request) =>
        response.Id == request.Id && response.Questions.SequenceEqual(request.Questions);

    /// <summary>
    ///     This method sends one query over UDP and returns the queried server's reply, or
    ///     <see langword="null" /> on timeout, a socket error or a malformed reply. The source
    ///     check adds to the ID and question check: an off-path attacker can guess a 16-bit ID,
    ///     so the ID alone is not enough.
    /// </summary>
    private async Task<DnsMessage> QueryUdpAsync(
        IPAddress serverAddress,
        DnsMessage request,
        byte[] requestBytes,
        CancellationToken stoppingToken
    )
    {
        IPEndPoint endpoint = new(serverAddress, serverPort);

        if (socketPool is null)
            return await QueryUdpWithFreshSocketAsync(endpoint, requestBytes, stoppingToken);

        using OutboundUdpSocketLease lease = await socketPool.RentAsync(
            serverAddress.AddressFamily,
            stoppingToken
        );

        try
        {
            await lease.Socket.SendToAsync(requestBytes, endpoint, stoppingToken);

            byte[] buffer = new byte[MaximumUdpResponseSize];

            // A pooled socket is unconnected, since it is reused across servers, so the OS does
            // not filter senders. A datagram from anyone else is logged and skipped, and the
            // loop keeps waiting until the timeout. So is one that does not answer this query:
            // a late reply to an earlier query on the same socket, or a spoofed one.
            while (!stoppingToken.IsCancellationRequested)
            {
                SocketReceiveFromResult result = await lease.Socket.ReceiveFromAsync(
                    buffer,
                    endpoint,
                    stoppingToken
                );

                if (!Equals(result.RemoteEndPoint, endpoint))
                {
                    logger?.LogDebug(
                        "Discarding a UDP response to an upstream query: it arrived from "
                            + "{ActualEndpoint}, not the queried {ExpectedEndpoint}.",
                        result.RemoteEndPoint,
                        endpoint
                    );
                    continue;
                }

                DnsMessage response = DnsMessageReader.Read(buffer.AsSpan(0, result.ReceivedBytes));

                if (!IsResponseTo(response, request))
                {
                    logger?.LogDebug(
                        "Discarding a UDP response to an upstream query from {Endpoint}: its "
                            + "message ID or question does not match the query.",
                        endpoint
                    );
                    continue;
                }

                return response;
            }

            return null;
        }
        catch (Exception exception)
            when (exception is SocketException or OperationCanceledException or FormatException)
        {
            if (exception is SocketException)
                lease.MarkUnhealthy();

            return null;
        }
    }

    /// <summary>
    ///     This method queries over a new connected socket on an OS-chosen port, for when no
    ///     <see cref="IOutboundUdpSocketPool" /> was supplied. (With the pool,
    ///     <see cref="IOutboundUdpSocketPool.RentAsync" /> hands out a one-off socket when
    ///     <c>OutboundQueryOptions.EnableSourcePortPool</c> is off.)
    /// </summary>
    private static async Task<DnsMessage> QueryUdpWithFreshSocketAsync(
        IPEndPoint endpoint,
        byte[] requestBytes,
        CancellationToken stoppingToken
    )
    {
        try
        {
            using Socket socket = new(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            // A connected UDP socket only receives from its peer, so the OS filters other
            // senders.
            await socket.ConnectAsync(endpoint, stoppingToken);
            await socket.SendAsync(requestBytes, stoppingToken);

            byte[] buffer = new byte[MaximumUdpResponseSize];
            int received = await socket.ReceiveAsync(buffer, stoppingToken);

            return DnsMessageReader.Read(buffer.AsSpan(0, received));
        }
        catch (Exception exception)
            when (exception is SocketException or OperationCanceledException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This method sends one query over a new TCP connection and reads back one
    ///     length-prefixed response (RFC 1035 §4.2.2), or returns <see langword="null" /> on any
    ///     failure.
    /// </summary>
    private async Task<DnsMessage> QueryTcpAsync(
        IPAddress serverAddress,
        byte[] requestBytes,
        CancellationToken stoppingToken
    )
    {
        try
        {
            using Socket socket = new(
                serverAddress.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp
            );
            await socket.ConnectAsync(new IPEndPoint(serverAddress, serverPort), stoppingToken);

            byte[] lengthPrefix = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)requestBytes.Length);
            await socket.SendAsync(lengthPrefix, stoppingToken);
            await socket.SendAsync(requestBytes, stoppingToken);

            byte[] responseLengthBuffer = new byte[2];
            await ReadExactAsync(socket, responseLengthBuffer, stoppingToken);
            ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(responseLengthBuffer);

            byte[] responseBuffer = new byte[responseLength];
            await ReadExactAsync(socket, responseBuffer, stoppingToken);

            return DnsMessageReader.Read(responseBuffer);
        }
        catch (Exception exception)
            when (exception
                    is SocketException
                        or OperationCanceledException
                        or FormatException
                        or IOException
            )
        {
            return null;
        }
    }

    /// <summary>
    ///     This method fills a buffer completely from a socket, throwing if the connection closes
    ///     before it could.
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
                throw new IOException("The connection closed before a complete response arrived.");

            totalRead += read;
        }
    }
}

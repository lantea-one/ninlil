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
///     This class implements <see cref="IUpstreamDnsClient" /> with real UDP and TCP sockets.
/// </summary>
/// <param name="options">
///     This client's own per-query timeout, read live via <see cref="IOptionsMonitor{TOptions}" />
///     -- see <see cref="Resolution.RecursiveQueryResolver" />'s own doc comment for why this
///     client, registered as a singleton, cannot use the usual snapshotted
///     <see cref="IOptions{TOptions}" /> instead.
/// </param>
/// <param name="socketPool">
///     The shared, source-port-pooling outbound socket pool this client's own UDP queries are
///     sent through -- see <see cref="IOutboundUdpSocketPool" />'s own doc comment. Defaults to
///     <see langword="null" /> so a caller/test that constructs this client directly (bypassing
///     dependency injection) keeps compiling and running unchanged, falling back to this class's
///     own pre-pool behavior: a fresh, one-off, OS-ephemeral-port socket per query.
/// </param>
/// <param name="logger">
///     The logger this client records a dropped, mismatched (spoofed or stray) response to.
///     Defaults to <see langword="null" />, in which case a mismatch is silently dropped without
///     logging -- the same "every optional cross-cutting dependency degrades gracefully, never
///     throws, when absent" idiom this project's other <see langword="null" />-defaulted
///     constructor parameters already establish.
/// </param>
public sealed class UpstreamDnsClient(
    IOptionsMonitor<RecursiveServerOptions> options,
    IOutboundUdpSocketPool socketPool = null,
    ILogger<UpstreamDnsClient> logger = null
) : IUpstreamDnsClient
{
    /// <summary>
    ///     This constant contains the IANA-assigned port number for DNS.
    /// </summary>
    private const int DnsPort = 53;

    /// <summary>
    ///     This constant contains the largest buffer a UDP response is read into. 65527 octets is
    ///     the theoretical maximum a UDP payload can occupy.
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
            // This server is doing the recursing itself; each individual query sent to an
            // upstream authoritative server is an ordinary, non-recursive lookup -- setting RD
            // here would ask that server to recurse on our behalf too, which authoritative
            // servers correctly ignore anyway, but is not what an iterative resolver means to ask.
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

        DnsMessage response = await QueryUdpAsync(serverAddress, requestBytes, timeoutCts.Token);

        if (response is { IsTruncated: true })
            response =
                await QueryTcpAsync(serverAddress, requestBytes, timeoutCts.Token) ?? response;

        // A reply whose ID doesn't match what we asked is either a stray, unrelated packet or a
        // spoofing attempt -- either way, not a real answer to this query.
        return response is not null && response.Id == id ? response : null;
    }

    /// <summary>
    ///     This method sends one query over UDP and reads back a single response, validating that
    ///     it actually arrived from the name server queried before accepting it -- a real
    ///     anti-spoofing check, not merely the transaction-ID comparison <see cref="QueryAsync" />
    ///     already does on top of this one, since an off-path attacker guessing a 16-bit
    ///     transaction ID is a real, historically exploited threat this project treats as
    ///     survivable only in combination with source-address validation and source-port
    ///     unpredictability, not either alone.
    /// </summary>
    private async Task<DnsMessage> QueryUdpAsync(
        IPAddress serverAddress,
        byte[] requestBytes,
        CancellationToken stoppingToken
    )
    {
        IPEndPoint endpoint = new(serverAddress, DnsPort);

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

            // A pooled socket is unconnected (it outlives any single remote peer across its own
            // reuse), so nothing at the OS level filters out a reply from anyone other than
            // serverAddress -- checked explicitly here instead. A mismatched sender is logged and
            // discarded, and this loop keeps waiting for the real reply until stoppingToken's own
            // deadline elapses, rather than either trusting the first datagram to arrive or
            // giving up on the very first stray one.
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

                return DnsMessageReader.Read(buffer.AsSpan(0, result.ReceivedBytes));
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
    ///     This method preserves this client's own pre-pool behavior: a fresh, one-off,
    ///     OS-ephemeral-port, connected socket per query -- used only when
    ///     <see cref="IOutboundUdpSocketPool" /> is unavailable (a caller bypassing dependency
    ///     injection), since <see cref="IOutboundUdpSocketPool.RentAsync" /> itself already
    ///     returns an equivalent one-off socket whenever
    ///     <c>OutboundQueryOptions.EnableSourcePortPool</c> is off.
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

            // Connecting a UDP socket sets its default peer and filters out anything arriving
            // from any other address:port -- basic protection against unrelated or spoofed
            // traffic landing on this same locally-bound socket.
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
    ///     This method sends one query over a fresh TCP connection and reads back a single,
    ///     length-prefixed response, per RFC 1035 §4.2.2.
    /// </summary>
    private static async Task<DnsMessage> QueryTcpAsync(
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
            await socket.ConnectAsync(new IPEndPoint(serverAddress, DnsPort), stoppingToken);

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

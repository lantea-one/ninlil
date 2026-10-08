using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class learns a <see cref="ZoneTypeEnum.Stub" /> zone's NS records from its primary. A
///     stub zone only needs to know the zone's name servers, so this is an ordinary NS query, not
///     AXFR/IXFR; it shares only the RFC 1035 §4.2.2 TCP framing with
///     <see cref="ZoneTransferPullCodec" />. <c>SecondaryZoneRefreshService</c> calls it for stub
///     zones.
///     <para>
///         It always queries over UDP first, falling back to TCP when the answer is truncated,
///         whatever the zone's <c>ZoneEntity.TransferTransport</c>, like the SOA check in
///         <see cref="ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync" />.
///     </para>
/// </summary>
public static class StubZoneNsPuller
{
    /// <summary>
    ///     This method queries a primary for a zone's NS records, retrying over TCP if the UDP
    ///     answer is truncated.
    /// </summary>
    /// <param name="primaryAddress">The primary to query.</param>
    /// <param name="zoneName">The delegation to learn the NS records of.</param>
    /// <param name="tsigKeyName">
    ///     The key to sign the query with, or <see langword="null" /> to send it unsigned and
    ///     accept an unsigned answer. With a key, the answer must be signed with that key; an
    ///     unsigned or differently signed answer fails the pull.
    /// </param>
    /// <param name="primaryPort">The port the primary listens for ordinary DNS queries on.</param>
    /// <param name="udpTimeout">How long to wait for the initial UDP response.</param>
    /// <param name="tcpConnectTimeout">How long to wait for the TCP fallback to connect.</param>
    /// <param name="tcpReadTimeout">How long to wait for the TCP fallback's response.</param>
    /// <param name="tsigService">The service used to sign the query and verify the response.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The outcome of the pull.</returns>
    public static async Task<StubZoneNsPullResult> PullNsRecordsAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        string tsigKeyName,
        int primaryPort,
        TimeSpan udpTimeout,
        TimeSpan tcpConnectTimeout,
        TimeSpan tcpReadTimeout,
        ITsigService tsigService,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage query = new()
        {
            Id = ZoneTransferPullCodec.NewMessageId(),
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = false,
            Questions =
            {
                new DnsQuestion(zoneName, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet),
            },
            // Advertise a larger UDP payload so the NS set and glue rarely need the TCP fallback.
            Edns = new DnsEdnsOptions(0, 4096, false, []),
        };

        if (!string.IsNullOrEmpty(tsigKeyName))
            query = await tsigService.SignAsync(query, tsigKeyName, query.Id, stoppingToken);

        byte[] queryBytes = DnsMessageWriter.Write(query);

        (DnsMessage Message, byte[] RawBytes, int TsigOffset)? current = await QueryOverUdpAsync(
            primaryAddress,
            primaryPort,
            queryBytes,
            udpTimeout,
            stoppingToken
        );

        if (current is null)
            return StubZoneNsPullResult.Failure(
                $"Could not reach primary {primaryAddress} for an NS query."
            );

        if (current.Value.Message.IsTruncated)
        {
            current = await QueryOverTcpAsync(
                primaryAddress,
                primaryPort,
                queryBytes,
                tcpConnectTimeout,
                tcpReadTimeout,
                stoppingToken
            );

            if (current is null)
                return StubZoneNsPullResult.Failure(
                    $"Could not reach primary {primaryAddress} over TCP after a truncated NS query response."
                );
        }

        (DnsMessage message, byte[] rawBytes, int tsigOffset) = current.Value;

        if (message.Id != query.Id)
            return StubZoneNsPullResult.Failure(
                "The primary's NS query response carried a mismatched message identifier."
            );

        if (message.ResponseCode != DnsResponseCodeEnum.NoError)
            return StubZoneNsPullResult.Failure(
                $"Primary rejected the NS query: {message.ResponseCode}."
            );

        // With a key configured the answer must be signed by that key over the query's MAC
        // (RFC 8945 §5.3). The query pipeline signs the reply to every signed query, so an
        // unsigned answer is a spoof or a misconfigured primary.
        if (!string.IsNullOrEmpty(tsigKeyName))
        {
            if (message.Tsig is null)
                return StubZoneNsPullResult.Failure(
                    "Primary did not sign its NS query response, but a TSIG key is configured for this zone."
                );

            TsigVerificationResult tsigResult = await tsigService.VerifyResponseAsync(
                rawBytes,
                tsigOffset,
                message,
                query.Tsig.Mac,
                tsigKeyName,
                stoppingToken
            );

            if (!tsigResult.IsValid)
                return StubZoneNsPullResult.Failure(
                    $"Primary's NS query response failed TSIG verification: {tsigResult.Error}."
                );
        }

        // The NS records are in the Answer section; any glue is in the Additional section.
        List<DnsResourceRecord> records =
        [
            .. message.Answers.Where(record => record.Type == DnsResourceRecordTypeEnum.NS),
            .. message.AdditionalRecords.Where(record =>
                record.Type is DnsResourceRecordTypeEnum.A or DnsResourceRecordTypeEnum.Aaaa
            ),
        ];

        return records.Count == 0
            ? StubZoneNsPullResult.Failure($"Primary returned no NS records for '{zoneName}'.")
            : StubZoneNsPullResult.Success(records);
    }

    /// <summary>
    ///     This method sends the query over plain UDP and waits for a single response.
    /// </summary>
    private static async Task<(
        DnsMessage Message,
        byte[] RawBytes,
        int TsigOffset
    )?> QueryOverUdpAsync(
        IPAddress primaryAddress,
        int primaryPort,
        byte[] queryBytes,
        TimeSpan timeout,
        CancellationToken stoppingToken
    )
    {
        IPEndPoint remoteEndPoint = new(primaryAddress, primaryPort);
        using UdpClient client = new(primaryAddress.AddressFamily);

        try
        {
            await client.SendAsync(queryBytes, remoteEndPoint, stoppingToken);

            using CancellationTokenSource timeoutCts = new(timeout);
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

            UdpReceiveResult result = await client.ReceiveAsync(linkedCts.Token);
            DnsMessage message = DnsMessageReader.Read(result.Buffer, out int tsigOffset);

            return (message, result.Buffer, tsigOffset);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or SocketException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This method sends the query over TCP (RFC 1035 §4.2.2 framing) and waits for one
    ///     response; used when the UDP answer was truncated.
    /// </summary>
    private static async Task<(
        DnsMessage Message,
        byte[] RawBytes,
        int TsigOffset
    )?> QueryOverTcpAsync(
        IPAddress primaryAddress,
        int primaryPort,
        byte[] queryBytes,
        TimeSpan connectTimeout,
        TimeSpan readTimeout,
        CancellationToken stoppingToken
    )
    {
        using TcpClient tcpClient = new(primaryAddress.AddressFamily);

        try
        {
            using CancellationTokenSource connectTimeoutCts = new(connectTimeout);
            using CancellationTokenSource connectLinkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    connectTimeoutCts.Token
                );

            await tcpClient.ConnectAsync(primaryAddress, primaryPort, connectLinkedCts.Token);

            NetworkStream stream = tcpClient.GetStream();
            await ZoneTransferPullCodec.WriteFramedMessageAsync(stream, queryBytes, stoppingToken);
            byte[] responseBytes = await ZoneTransferPullCodec.ReadFramedMessageAsync(
                stream,
                readTimeout,
                stoppingToken
            );

            if (responseBytes is null)
                return null;

            DnsMessage message = DnsMessageReader.Read(responseBytes, out int tsigOffset);

            return (message, responseBytes, tsigOffset);
        }
        catch (Exception exception)
            when (exception
                    is SocketException
                        or OperationCanceledException
                        or IOException
                        or TimeoutException
                        or FormatException
            )
        {
            return null;
        }
        finally
        {
            tcpClient.Close();
        }
    }
}

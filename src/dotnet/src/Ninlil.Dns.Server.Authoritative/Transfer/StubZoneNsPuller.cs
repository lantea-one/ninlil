using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class learns a <see cref="ZoneTypeEnum.Stub" /> zone's own delegation NS records from
///     its configured primary -- a genuinely different exchange than <see cref="IZoneTransferClient" />'s
///     own RFC 5936/1995 AXFR/IXFR pull, since a stub zone deliberately never learns the
///     delegation's full content, only who its authoritative name servers are (see
///     <see cref="ZoneTypeEnum.Stub" />'s own doc comment). This is simply an ordinary NS-type
///     query, exactly like any other DNS lookup a resolver would send -- there is no AXFR/IXFR
///     opcode involved at all -- so it deliberately reuses none of <see cref="ZoneTransferPullCodec" />'s
///     AXFR/IXFR-specific query-building or multi-message-sequence logic, only the RFC 1035
///     §4.2.2 TCP framing helpers every stream-based DNS exchange in this project already shares.
///     <para>
///         Like <see cref="ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync" />'s own lightweight
///         SOA probe, this deliberately always queries over plain UDP first (falling back to TCP
///         only if the response is truncated), regardless of a zone's own configured
///         <c>ZoneEntity.TransferTransport</c> -- an ordinary NS lookup carries no content worth
///         adding transport confidentiality to beyond what TSIG (when configured) already
///         authenticates.
///     </para>
/// </summary>
public static class StubZoneNsPuller
{
    /// <summary>
    ///     This method queries a primary for a zone's own NS records, falling back from UDP to
    ///     TCP if the initial response is truncated.
    /// </summary>
    /// <param name="primaryAddress">The primary to query.</param>
    /// <param name="zoneName">The delegation to learn the NS records of.</param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key to sign the query with, or <see langword="null" /> to send it
    ///     unsigned. A response that itself carries a TSIG record is always verified against this
    ///     same key, but one that carries none is tolerated rather than rejected -- see this
    ///     method's own body comment for why an ordinary NS query's response is never actually
    ///     signed by this project's own primary.
    /// </param>
    /// <param name="primaryPort">The port the primary listens for ordinary DNS queries on.</param>
    /// <param name="udpTimeout">How long to wait for the initial UDP response.</param>
    /// <param name="tcpConnectTimeout">How long to wait for the TCP fallback's own connection.</param>
    /// <param name="tcpReadTimeout">How long to wait for the TCP fallback's own response.</param>
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
            // A larger-than-default UDP payload size buffer reduces the odds that an ordinary NS
            // RRset (plus any address glue the primary includes) needs the slower TCP fallback
            // below at all.
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

        // An ordinary NS-type query is answered by this project's own AuthoritativeQueryResolver
        // -- a genuinely different code path than ZoneTransferService's own AXFR/IXFR responder,
        // and one that never signs its response even when the request itself carried a valid
        // TSIG record (TSIG-aware response generation is scoped, deliberately, to zone
        // transfer/RFC 2136 dynamic update only -- see ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync's
        // own identical, already-established precedent for exactly this same lightweight-query
        // shape). Requiring a signed response here would therefore reject a real answer from this
        // project's own primary outright; instead, the response is verified only when it actually
        // carries a TSIG record at all (catching a spoofed or tampered signed response), and
        // tolerated, unverified, when it carries none.
        if (!string.IsNullOrEmpty(tsigKeyName) && message.Tsig is not null)
        {
            TsigVerificationResult tsigResult = await tsigService.VerifyAsync(
                rawBytes,
                tsigOffset,
                message,
                stoppingToken
            );

            if (!tsigResult.IsValid)
                return StubZoneNsPullResult.Failure(
                    $"Primary's NS query response failed TSIG verification: {tsigResult.Error}."
                );
        }

        // The NS RRset itself lands in the Answer section (an ordinary, direct positive answer,
        // not a referral); any address glue the primary chooses to include for those name
        // servers lands in the Additional section, exactly like glue accompanying a delegation
        // referral elsewhere in this project.
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
    ///     This method sends the query over plain TCP (RFC 1035 §4.2.2 framing) and waits for a
    ///     single response -- the fallback path a truncated UDP response triggers.
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

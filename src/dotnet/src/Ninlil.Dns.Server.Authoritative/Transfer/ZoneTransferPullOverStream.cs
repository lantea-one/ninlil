using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class runs the AXFR/IXFR exchange over a connected <see cref="Stream" />
///     (<see cref="System.Net.Sockets.NetworkStream" />, <see cref="System.Net.Security.SslStream" />
///     or <see cref="System.Net.Quic.QuicStream" />), shared by the <see cref="IZoneTransferClient" />s:
///     write the framed query, read framed responses until the primary closes the connection,
///     verify TSIG when a key is set, and interpret the result with
///     <see cref="ZoneTransferPullCodec.BuildResult" />.
///     <para>
///     The first response is verified against the request MAC
///     (<see cref="ITsigService.VerifyResponseAsync" />, RFC 8945 §5.3). Each later signed message
///     chains from the last signed one (<see cref="ITsigService.VerifyContinuationAsync" />). Up to
///     99 unsigned messages in a row are accepted (RFC 8945 §5.3.1), but the last message must be
///     signed.
///     </para>
/// </summary>
internal static class ZoneTransferPullOverStream
{
    /// <summary>
    ///     This constant contains the RFC 8945 §5.3.1 limit: a verifier must accept up to 99
    ///     unsigned messages in a row, as long as the first and last are signed.
    /// </summary>
    private const int MaxConsecutiveUnsignedMessages = 99;

    /// <summary>
    ///     This method performs the exchange described in the class summary.
    /// </summary>
    /// <param name="stream">The already-connected stream to pull the transfer over.</param>
    /// <param name="queryBytes">
    ///     The encoded AXFR/IXFR query, already TSIG-signed when <paramref name="tsigKeyName" /> is
    ///     set.
    /// </param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key every response must verify against, or <see langword="null" /> to
    ///     accept an unsigned response.
    /// </param>
    /// <param name="requestMac">
    ///     The MAC of the query's TSIG record, used to verify the first response (RFC 8945 §5.3), or
    ///     <see langword="null" /> when <paramref name="tsigKeyName" /> is <see langword="null" />.
    /// </param>
    /// <param name="currentSerial">This server's current serial, passed to <see cref="ZoneTransferPullCodec.BuildResult" /> unchanged.</param>
    /// <param name="tsigService">The service used to verify each response's TSIG.</param>
    /// <param name="readTimeout">How long to wait for each response message.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task<ZoneTransferPullResult> PullAsync(
        Stream stream,
        byte[] queryBytes,
        string tsigKeyName,
        byte[] requestMac,
        uint? currentSerial,
        ITsigService tsigService,
        TimeSpan readTimeout,
        CancellationToken stoppingToken
    )
    {
        await WriteQueryAsync(stream, queryBytes, stoppingToken);

        return await ReadResponsesAsync(
            stream,
            tsigKeyName,
            requestMac,
            currentSerial,
            tsigService,
            readTimeout,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method writes only the framed query, so <see cref="QuicZoneTransferClient" /> can
    ///     close its stream's write side (RFC 9250 §4.2) before reading the responses.
    /// </summary>
    public static Task WriteQueryAsync(
        Stream stream,
        byte[] queryBytes,
        CancellationToken stoppingToken
    ) => ZoneTransferPullCodec.WriteFramedMessageAsync(stream, queryBytes, stoppingToken);

    /// <summary>
    ///     This method reads framed responses until the connection closes, verifies TSIG when
    ///     required, and interprets the result: the read half of <see cref="PullAsync" />.
    /// </summary>
    /// <param name="stream">The already-connected stream to read the transfer response over.</param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key every response must verify against, or <see langword="null" /> to
    ///     accept an unsigned response.
    /// </param>
    /// <param name="requestMac">
    ///     The MAC of the query's TSIG record, used to verify the first response (RFC 8945 §5.3), or
    ///     <see langword="null" /> when <paramref name="tsigKeyName" /> is <see langword="null" />.
    /// </param>
    /// <param name="currentSerial">This server's current serial, passed to <see cref="ZoneTransferPullCodec.BuildResult" /> unchanged.</param>
    /// <param name="tsigService">The service used to verify each response's TSIG.</param>
    /// <param name="readTimeout">How long to wait for each response message.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task<ZoneTransferPullResult> ReadResponsesAsync(
        Stream stream,
        string tsigKeyName,
        byte[] requestMac,
        uint? currentSerial,
        ITsigService tsigService,
        TimeSpan readTimeout,
        CancellationToken stoppingToken
    )
    {
        List<DnsResourceRecord> flatAnswers = [];
        int messageCount = 0;

        // RFC 8945 §5.3.1 chain state: the MAC of the last signed message (the request MAC to
        // start with) and the bytes of unsigned messages since then, up to
        // MaxConsecutiveUnsignedMessages. ZoneTransferService signs every message, but other
        // primaries may not.
        byte[] priorMac = null;
        List<byte[]> pendingUnsignedMessageBytes = [];

        while (true)
        {
            byte[] responseBytes;

            try
            {
                responseBytes = await ZoneTransferPullCodec.ReadFramedMessageAsync(
                    stream,
                    readTimeout,
                    stoppingToken
                );
            }
            catch (Exception exception)
                when (exception is TimeoutException or IOException or FormatException)
            {
                return ZoneTransferPullResult.Failure(
                    $"Reading the transfer response failed: {exception.Message}"
                );
            }

            // Null means the primary closed the connection, which ends the transfer.
            if (responseBytes is null)
                break;

            messageCount++;
            DnsMessage response = DnsMessageReader.Read(responseBytes, out int tsigOffset);

            if (response.ResponseCode != DnsResponseCodeEnum.NoError)
                return ZoneTransferPullResult.Failure(
                    $"Primary rejected the transfer request: {response.ResponseCode}."
                );

            if (!string.IsNullOrEmpty(tsigKeyName))
            {
                if (response.Tsig is null)
                {
                    // RFC 8945 §5.3.1: the first message must be signed.
                    if (messageCount == 1)
                        return ZoneTransferPullResult.Failure(
                            "Primary did not sign the first response message, but a TSIG key is configured for this zone."
                        );

                    pendingUnsignedMessageBytes.Add(responseBytes);

                    if (pendingUnsignedMessageBytes.Count > MaxConsecutiveUnsignedMessages)
                        return ZoneTransferPullResult.Failure(
                            $"Primary sent more than {MaxConsecutiveUnsignedMessages} consecutive unsigned messages within a TSIG-protected transfer."
                        );

                    flatAnswers.AddRange(response.Answers);
                    continue;
                }

                TsigVerificationResult tsigResult =
                    messageCount == 1
                        ? await tsigService.VerifyResponseAsync(
                            responseBytes,
                            tsigOffset,
                            response,
                            requestMac,
                            stoppingToken
                        )
                        : await tsigService.VerifyContinuationAsync(
                            responseBytes,
                            tsigOffset,
                            response,
                            priorMac,
                            pendingUnsignedMessageBytes,
                            stoppingToken
                        );

                if (!tsigResult.IsValid)
                    return ZoneTransferPullResult.Failure(
                        $"Primary's response failed TSIG verification: {tsigResult.Error}."
                    );

                priorMac = response.Tsig.Mac;
                pendingUnsignedMessageBytes = [];
            }

            flatAnswers.AddRange(response.Answers);
        }

        // RFC 8945 §5.3.1: the last message must be signed too, so unsigned messages still
        // pending at the end fail the pull.
        if (!string.IsNullOrEmpty(tsigKeyName) && pendingUnsignedMessageBytes.Count > 0)
            return ZoneTransferPullResult.Failure(
                "Primary's transfer ended without a signed final message, but a TSIG key is configured for this zone."
            );

        return ZoneTransferPullCodec.BuildResult(
            flatAnswers,
            currentSerial,
            wasMultiMessage: messageCount > 1
        );
    }
}

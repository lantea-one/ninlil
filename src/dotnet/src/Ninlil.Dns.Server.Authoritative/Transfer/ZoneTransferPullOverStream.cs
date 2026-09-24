using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class runs the actual AXFR/IXFR request/response exchange over an already-established
///     <see cref="Stream" />-shaped connection -- the one piece of pull logic every
///     <see cref="IZoneTransferClient" /> implementation shares once its own transport-specific
///     connection setup (a plain <see cref="System.Net.Sockets.NetworkStream" />, an
///     <see cref="System.Net.Security.SslStream" />, or a <see cref="System.Net.Quic.QuicStream" />)
///     has already handed it a live stream: write the framed query, read every framed response
///     message until the connection signals completion, verify each one's TSIG signature per RFC
///     8945 §5.3.1's own multi-message chaining rule when one is required, and interpret the
///     fully-reassembled answer sequence via <see cref="ZoneTransferPullCodec.BuildResult" />.
///     <para>
///         The first response message is verified against the original request's own MAC (RFC
///         8945 §5.3's ordinary "Generation of TSIG on Answers" formula, via
///         <see cref="ITsigService.VerifyResponseAsync" />); every later message chains from
///         whichever earlier message most recently carried a TSIG (<see cref="ITsigService.VerifyContinuationAsync" />),
///         tolerating up to 99 consecutive unsigned messages in between per RFC 8945 §5.4, as long
///         as the sequence's own last message -- the one immediately preceding the primary closing
///         the connection -- carries a verifying TSIG of its own.
///     </para>
/// </summary>
internal static class ZoneTransferPullOverStream
{
    /// <summary>
    ///     This constant contains RFC 8945 §5.4's own tolerance limit: a client verifying a
    ///     multi-message, TSIG-protected sequence "MUST accept up to 99 intermediary messages
    ///     without a TSIG", provided the sequence's own first and last message both carry one.
    /// </summary>
    private const int MaxConsecutiveUnsignedMessages = 99;

    /// <summary>
    ///     This method performs the exchange described in this class's own doc comment.
    /// </summary>
    /// <param name="stream">The already-connected stream to pull the transfer over.</param>
    /// <param name="queryBytes">
    ///     The already-built AXFR/IXFR query's own encoded bytes -- already TSIG-signed, when
    ///     <paramref name="tsigKeyName" /> is set, before this method is ever called.
    /// </param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key every response must verify against, or <see langword="null" /> to
    ///     accept an unsigned response.
    /// </param>
    /// <param name="requestMac">
    ///     The MAC from <paramref name="queryBytes" />'s own TSIG record (already computed by the
    ///     caller when it signed the query), used to verify the first response message per RFC
    ///     8945 §5.3 -- <see langword="null" /> when <paramref name="tsigKeyName" /> is
    ///     <see langword="null" />.
    /// </param>
    /// <param name="currentSerial">This server's own current serial, forwarded to <see cref="ZoneTransferPullCodec.BuildResult" /> unchanged.</param>
    /// <param name="tsigService">The service used to verify each response's own TSIG signature.</param>
    /// <param name="readTimeout">How long to wait for each individual response message.</param>
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
    ///     This method writes just the framed query, split out from <see cref="PullAsync" /> so
    ///     <see cref="QuicZoneTransferClient" /> can signal its own stream's write side complete
    ///     (RFC 9250 §4.2.1) in between writing the query and reading the response sequence -- a
    ///     step the plain-TCP/TLS transports have no equivalent of.
    /// </summary>
    public static Task WriteQueryAsync(
        Stream stream,
        byte[] queryBytes,
        CancellationToken stoppingToken
    ) => ZoneTransferPullCodec.WriteFramedMessageAsync(stream, queryBytes, stoppingToken);

    /// <summary>
    ///     This method reads every framed response message until the connection signals
    ///     completion, verifying each one's TSIG signature (per RFC 8945 §5.3.1's own
    ///     multi-message chaining rule) when one is required, and interprets the
    ///     fully-reassembled answer sequence -- the read half of <see cref="PullAsync" />, split
    ///     out for the same reason <see cref="WriteQueryAsync" /> is.
    /// </summary>
    /// <param name="stream">The already-connected stream to read the transfer response over.</param>
    /// <param name="tsigKeyName">
    ///     The pre-shared key every response must verify against, or <see langword="null" /> to
    ///     accept an unsigned response.
    /// </param>
    /// <param name="requestMac">
    ///     The MAC from the original query's own TSIG record, used to verify the first response
    ///     message per RFC 8945 §5.3 -- <see langword="null" /> when <paramref name="tsigKeyName" />
    ///     is <see langword="null" />.
    /// </param>
    /// <param name="currentSerial">This server's own current serial, forwarded to <see cref="ZoneTransferPullCodec.BuildResult" /> unchanged.</param>
    /// <param name="tsigService">The service used to verify each response's own TSIG signature.</param>
    /// <param name="readTimeout">How long to wait for each individual response message.</param>
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

        // RFC 8945 §5.3.1's own running chain state: the MAC from whichever message most recently
        // carried a TSIG (seeded, for the very first message, by the request's own MAC instead --
        // see priorMac's own use below), and the raw bytes of every message received since then
        // that carried none at all, tolerated per RFC 8945 §5.4 up to
        // MaxConsecutiveUnsignedMessages in a row. This project's own outbound path
        // (ZoneTransferService) never actually produces an unsigned intermediate message -- it
        // always signs every one -- but a real-world peer is fully entitled to leave some
        // unsigned instead, so this loop still has to tolerate that shape to genuinely
        // interoperate with one.
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

            // A null result means the primary closed the connection -- the normal, expected way a
            // transfer ends, whether it took one message or many.
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
                    // RFC 8945 §5.4 requires the very first message to carry a TSIG unconditionally
                    // -- there is no earlier message in the sequence for a first, unsigned one to
                    // possibly chain from.
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

        // RFC 8945 §5.4 also requires the very last message to carry a valid TSIG -- if the
        // sequence ended with one or more unsigned messages still pending (never followed by a
        // signed one to verify them against), that requirement was never actually met.
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

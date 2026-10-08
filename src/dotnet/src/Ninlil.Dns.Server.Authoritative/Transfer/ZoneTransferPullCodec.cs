using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class holds the protocol logic the <see cref="IZoneTransferClient" />s share, whatever
///     carries the bytes (TCP, TLS or QUIC): building the AXFR/IXFR query, RFC 1035 §4.2.2
///     two-octet length framing over any <see cref="Stream" /> (<see cref="NetworkStream" />,
///     <see cref="System.Net.Security.SslStream" /> and <see cref="System.Net.Quic.QuicStream" /> are
///     all streams), the UDP SOA check, and interpreting the reassembled answer.
/// </summary>
internal static class ZoneTransferPullCodec
{
    /// <summary>
    ///     This method generates a random message ID.
    /// </summary>
    public static ushort NewMessageId() => (ushort)Random.Shared.Next(ushort.MaxValue + 1);

    /// <summary>
    ///     This method queries a primary's current SOA serial over plain UDP. Every client uses it,
    ///     including the TLS and QUIC ones: the serial is not sensitive, while the zone content the
    ///     pull carries is.
    /// </summary>
    public static async Task<uint?> QuerySoaSerialOverUdpAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        string tsigKeyName,
        int primaryPort,
        TimeSpan timeout,
        ITsigService tsigService,
        CancellationToken stoppingToken,
        ILogger logger = null
    )
    {
        DnsMessage query = new()
        {
            Id = NewMessageId(),
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = false,
            Questions =
            {
                new DnsQuestion(zoneName, DnsResourceRecordTypeEnum.Soa, DnsClassEnum.Internet),
            },
        };

        if (!string.IsNullOrEmpty(tsigKeyName))
            query = await tsigService.SignAsync(query, tsigKeyName, query.Id, stoppingToken);

        byte[] queryBytes = DnsMessageWriter.Write(query);
        IPEndPoint remoteEndPoint = new(primaryAddress, primaryPort);

        using UdpClient client = new(primaryAddress.AddressFamily);

        try
        {
            await client.SendAsync(queryBytes, remoteEndPoint, stoppingToken);

            using CancellationTokenSource timeoutCts = new(timeout);
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

            UdpReceiveResult result = await client.ReceiveAsync(linkedCts.Token);
            DnsMessage response = DnsMessageReader.Read(result.Buffer, out int tsigOffset);

            // With a key configured the answer must be signed by that key over the query's MAC;
            // an unsigned or wrongly signed answer could be spoofed, so it yields no serial.
            if (!string.IsNullOrEmpty(tsigKeyName))
            {
                if (response.Tsig is null)
                {
                    logger?.LogDebug(
                        "The SOA answer from {Primary} for {Zone} was not signed, but a TSIG key is configured.",
                        primaryAddress,
                        zoneName
                    );
                    return null;
                }

                TsigVerificationResult tsigResult = await tsigService.VerifyResponseAsync(
                    result.Buffer,
                    tsigOffset,
                    response,
                    query.Tsig.Mac,
                    tsigKeyName,
                    stoppingToken
                );

                if (!tsigResult.IsValid)
                {
                    logger?.LogDebug(
                        "The SOA answer from {Primary} for {Zone} failed TSIG verification: {Error}.",
                        primaryAddress,
                        zoneName,
                        tsigResult.Error
                    );
                    return null;
                }
            }

            return
                response.Id == query.Id
                && response.ResponseCode == DnsResponseCodeEnum.NoError
                && response.Answers.Count > 0
                && response.Answers[0].Type == DnsResourceRecordTypeEnum.Soa
                && DnsRecordDataCodecRegistry.Decode(
                    DnsResourceRecordTypeEnum.Soa,
                    response.Answers[0].Data
                )
                    is SoaRecordData soaData
                ? soaData.Serial
                : null;
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or SocketException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This method builds an AXFR or IXFR query, per whether a current serial is supplied.
    /// </summary>
    public static DnsMessage BuildTransferQuery(DnsName zoneName, uint? currentSerial)
    {
        DnsMessage query = new()
        {
            Id = NewMessageId(),
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    zoneName,
                    currentSerial is null
                        ? DnsResourceRecordTypeEnum.Axfr
                        : DnsResourceRecordTypeEnum.Ixfr,
                    DnsClassEnum.Internet
                ),
            },
        };

        if (currentSerial is { } serial)
            // RFC 1995 §3: the client's serial goes as an SOA in the Authority section. Only the
            // serial matters to the primary; the other fields are filled in because SoaRecordData
            // needs them.
            query.AuthorityRecords.Add(
                new DnsResourceRecord(
                    zoneName,
                    DnsResourceRecordTypeEnum.Soa,
                    DnsClassEnum.Internet,
                    0,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Soa,
                        new SoaRecordData(DnsName.Root, DnsName.Root, serial, 0, 0, 0, 0)
                    )
                )
            );

        return query;
    }

    /// <summary>
    ///     This method interprets the reassembled answer records, using the same rule
    ///     <see cref="ZoneTransferService" /> produces them by: in an incremental answer the leading
    ///     SOA is followed by another, older SOA; in a full one it is not.
    /// </summary>
    /// <param name="flatAnswers">Every answer record from every response message, in order, flattened into one list.</param>
    /// <param name="requestedSerial">The serial this client's pull request carried, if any (an IXFR).</param>
    /// <param name="wasMultiMessage">
    ///     Whether the answer spanned more than one message. Only then does
    ///     <see cref="ZoneTransferService" /> add a closing SOA to strip. In a single-message IXFR whose
    ///     last step only removes records, the step's closing SOA equals the leading SOA, so equality
    ///     alone cannot tell it from a closing bookend; stripping it there would break the diff.
    /// </param>
    public static ZoneTransferPullResult BuildResult(
        List<DnsResourceRecord> flatAnswers,
        uint? requestedSerial,
        bool wasMultiMessage
    )
    {
        if (flatAnswers.Count == 0 || flatAnswers[0].Type != DnsResourceRecordTypeEnum.Soa)
            return ZoneTransferPullResult.Failure(
                "The transfer response did not begin with an SOA record."
            );

        if (
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Soa, flatAnswers[0].Data)
            is not SoaRecordData leadingSoaData
        )
            return ZoneTransferPullResult.Failure(
                "The transfer response's leading SOA record could not be decoded."
            );

        uint finalSerial = leadingSoaData.Serial;

        // A multi-message answer ends with a repeat of the leading SOA; strip it. Only for
        // multi-message answers (see wasMultiMessage).
        if (
            wasMultiMessage
            && flatAnswers.Count > 1
            && flatAnswers[^1].Type == DnsResourceRecordTypeEnum.Soa
        )
        {
            DnsResourceRecord lastRecord = flatAnswers[^1];

            if (lastRecord == flatAnswers[0])
                flatAnswers.RemoveAt(flatAnswers.Count - 1);
        }

        if (flatAnswers.Count == 1 && requestedSerial == finalSerial)
            return ZoneTransferPullResult.AlreadyCurrent(finalSerial);

        bool isIncrementalShape =
            flatAnswers.Count > 1 && flatAnswers[1].Type == DnsResourceRecordTypeEnum.Soa;

        if (!isIncrementalShape)
            return ZoneTransferPullResult.Full(finalSerial, flatAnswers);

        List<ZoneTransferDiffStep> steps = [];
        int index = 1;

        while (index < flatAnswers.Count)
        {
            if (
                flatAnswers[index].Type != DnsResourceRecordTypeEnum.Soa
                || DnsRecordDataCodecRegistry.Decode(
                    DnsResourceRecordTypeEnum.Soa,
                    flatAnswers[index].Data
                )
                    is not SoaRecordData oldSoaData
            )
                return ZoneTransferPullResult.Failure(
                    "The incremental transfer response was malformed (expected an old-serial SOA)."
                );

            index++;
            List<DnsResourceRecord> removed = [];

            while (
                index < flatAnswers.Count
                && flatAnswers[index].Type != DnsResourceRecordTypeEnum.Soa
            )
            {
                removed.Add(flatAnswers[index]);
                index++;
            }

            if (
                index >= flatAnswers.Count
                || DnsRecordDataCodecRegistry.Decode(
                    DnsResourceRecordTypeEnum.Soa,
                    flatAnswers[index].Data
                )
                    is not SoaRecordData newSoaData
            )
                return ZoneTransferPullResult.Failure(
                    "The incremental transfer response was malformed (expected a new-serial SOA)."
                );

            index++;
            List<DnsResourceRecord> added = [];

            while (
                index < flatAnswers.Count
                && flatAnswers[index].Type != DnsResourceRecordTypeEnum.Soa
            )
            {
                added.Add(flatAnswers[index]);
                index++;
            }

            steps.Add(
                new ZoneTransferDiffStep(oldSoaData.Serial, removed, newSoaData.Serial, added)
            );
        }

        return ZoneTransferPullResult.Incremental(finalSerial, steps);
    }

    /// <summary>
    ///     This method writes one length-prefixed DNS message (RFC 1035 §4.2.2) to a stream.
    /// </summary>
    public static async Task WriteFramedMessageAsync(
        Stream stream,
        byte[] messageBytes,
        CancellationToken stoppingToken
    )
    {
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)messageBytes.Length);

        await stream.WriteAsync(lengthPrefix, stoppingToken);
        await stream.WriteAsync(messageBytes, stoppingToken);
    }

    /// <summary>
    ///     This method reads one length-prefixed DNS message.
    /// </summary>
    /// <returns>
    ///     The message bytes, or <see langword="null" /> if the connection closed before the next
    ///     message, which is how the pull detects the end of a transfer.
    /// </returns>
    /// <exception cref="TimeoutException">Thrown when no data arrives within <paramref name="readTimeout" />.</exception>
    /// <exception cref="IOException">
    ///     Thrown when the connection closes in the middle of a message.
    /// </exception>
    public static async Task<byte[]> ReadFramedMessageAsync(
        Stream stream,
        TimeSpan readTimeout,
        CancellationToken stoppingToken
    )
    {
        using CancellationTokenSource timeoutCts = new(readTimeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            timeoutCts.Token
        );

        try
        {
            byte[] lengthPrefix = new byte[2];

            if (
                !await ReadExactAsync(
                    stream,
                    lengthPrefix,
                    allowGracefulEmptyClose: true,
                    linkedCts.Token
                )
            )
                return null;

            ushort messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
            byte[] messageBytes = new byte[messageLength];

            if (
                !await ReadExactAsync(
                    stream,
                    messageBytes,
                    allowGracefulEmptyClose: false,
                    linkedCts.Token
                )
            )
                throw new IOException("The connection closed in the middle of a transfer message.");

            return messageBytes;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the next transfer message.");
        }
    }

    /// <summary>
    ///     This method fills a buffer completely, or reports that the connection closed instead.
    /// </summary>
    /// <param name="stream">The connection to read from.</param>
    /// <param name="buffer">The buffer to fill completely.</param>
    /// <param name="allowGracefulEmptyClose">
    ///     Whether a close before the first byte returns <see langword="false" /> instead of
    ///     throwing. True only when reading a length prefix, where a close ends the transfer.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        bool allowGracefulEmptyClose,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
                return totalRead == 0 && allowGracefulEmptyClose
                    ? false
                    : throw new IOException("The connection closed unexpectedly.");

            totalRead += read;
        }

        return true;
    }
}

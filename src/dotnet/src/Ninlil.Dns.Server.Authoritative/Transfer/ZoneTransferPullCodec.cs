using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class holds the wire-protocol logic every <see cref="IZoneTransferClient" />
///     implementation shares regardless of which transport actually carries the bytes (plain TCP,
///     TLS, or QUIC): building an AXFR/IXFR query, RFC 1035 §4.2.2's own 2-octet length-prefixed
///     message framing over any <see cref="Stream" />-shaped connection (a <see cref="NetworkStream" />,
///     an <see cref="System.Net.Security.SslStream" />, and a <see cref="System.Net.Quic.QuicStream" /> are all,
///     conveniently, real <see cref="Stream" /> subclasses), and interpreting a fully-reassembled
///     transfer response. Extracted from <see cref="TcpZoneTransferClient" />'s own original,
///     single-transport implementation once <see cref="TlsZoneTransferClient" />/
///     <see cref="QuicZoneTransferClient" /> needed the exact same logic around a differently
///     established connection.
/// </summary>
internal static class ZoneTransferPullCodec
{
    /// <summary>
    ///     This method generates a fresh, random message identifier.
    /// </summary>
    public static ushort NewMessageId() => (ushort)Random.Shared.Next(ushort.MaxValue + 1);

    /// <summary>
    ///     This method queries a primary's current SOA serial over plain UDP -- shared, unchanged,
    ///     by every <see cref="IZoneTransferClient" /> implementation regardless of which
    ///     transport it actually pulls a transfer over: a single, lightweight SOA lookup has no
    ///     real confidentiality need of its own, so this deliberately never encrypts, even for
    ///     <see cref="TlsZoneTransferClient" />/<see cref="QuicZoneTransferClient" />. The
    ///     transfer pull itself -- the actual zone content -- is what those two transports protect.
    /// </summary>
    public static async Task<uint?> QuerySoaSerialOverUdpAsync(
        IPAddress primaryAddress,
        DnsName zoneName,
        string tsigKeyName,
        int primaryPort,
        TimeSpan timeout,
        ITsigService tsigService,
        CancellationToken stoppingToken
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
            DnsMessage response = DnsMessageReader.Read(result.Buffer);

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
            // RFC 1995 §3: the client's own current serial travels as an SOA record in the
            // Authority section. Only the Serial field is meaningful to a correctly-implemented
            // primary; the rest are never inspected, but SoaRecordData has no fewer fields to
            // give it.
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
    ///     This method interprets a transfer's fully-reassembled answer sequence, per the same
    ///     shape-detection rule <see cref="ZoneTransferService" /> itself uses to produce one: an
    ///     incremental diff's leading SOA is immediately followed by another, older SOA, where a
    ///     full transfer's is not.
    /// </summary>
    /// <param name="flatAnswers">Every answer record from every response message, in order, flattened into one list.</param>
    /// <param name="requestedSerial">The serial this client's own pull request carried, if any (an IXFR).</param>
    /// <param name="wasMultiMessage">
    ///     Whether the transfer this sequence came from actually spanned more than one TCP-framed
    ///     response message -- <em>only</em> then does <see cref="ZoneTransferService" />'s own
    ///     producer ever append a genuinely redundant closing-SOA bookend
    ///     (<c>CreateAxfrResponseAsync</c>/<c>CreateIxfrResponseAsync</c> both gate that append on
    ///     <c>chunks.Count &gt; 1</c>) for this method to strip. A real, previously-undiscovered
    ///     bug this parameter fixes: for a genuinely single-message IXFR response whose own last
    ///     diff step happens to remove something with nothing added afterward, that step's own
    ///     legitimate closing "new serial" SOA is -- by construction -- byte-for-byte identical to
    ///     the leading current-SOA record (both are simply "the SOA at the zone's current serial"),
    ///     so content equality alone could not tell a real multi-message bookend apart from an
    ///     entirely ordinary, single-message diff structure; stripping it unconditionally corrupted
    ///     that structure into a malformed diff with no closing SOA at all. Found only by this
    ///     project's own real two-process catalog-zone smoke test pulling a real single-step,
    ///     removal-only IXFR -- no unit/integration test run before it ever exercised a real
    ///     multi-TCP-message transfer to notice the difference.
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

        // A multi-message transfer's very last record repeats the leading SOA as a completion
        // bookend; strip it so it isn't mistaken for a real, second copy of the zone's own SOA.
        // Gated on wasMultiMessage -- see this method's own parameter doc for why checking content
        // equality alone is not enough.
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
    ///     This method writes a single, length-prefixed DNS message, per RFC 1035 §4.2.2, to any
    ///     stream-shaped connection.
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
    ///     This method reads a single, length-prefixed DNS message.
    /// </summary>
    /// <returns>
    ///     The message's bytes, or <see langword="null" /> if the connection was closed before
    ///     any more data arrived -- the ordinary way a transfer response ends.
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
    ///     Whether a connection closing before even the first byte arrives is expected and should
    ///     report <see langword="false" /> rather than throwing -- true only when reading a new
    ///     message's length prefix, where that is exactly how a transfer is meant to end.
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

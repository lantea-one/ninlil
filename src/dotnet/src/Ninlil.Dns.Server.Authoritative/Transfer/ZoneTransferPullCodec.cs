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
///     all streams), and the UDP SOA check. The answer itself is interpreted by
///     <see cref="ZoneTransferResponseAssembler" />.
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
    ///     message. A closed connection is not the end of a transfer: a transfer ends at its closing
    ///     SOA (see <see cref="ZoneTransferResponseAssembler" />).
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
    ///     throwing. True only when reading a length prefix, where a close between messages is reported to the caller as a closed connection.
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

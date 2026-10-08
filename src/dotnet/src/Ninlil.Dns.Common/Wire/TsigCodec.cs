using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class builds the byte sequences that RFC 8945 §4.3 feeds into a TSIG message
///     authentication code. It only assembles wire bytes and does no cryptography. Computing and
///     verifying the MAC needs a shared secret and a key lookup, so it lives in
///     <c>Ninlil.Dns.Server.Tsig</c> instead. <c>TsigService</c> signs and verifies through
///     <see cref="BuildDigestInput" />, so both directions build the digest input the same way.
/// </summary>
public static class TsigCodec
{
    /// <summary>
    ///     This method builds the "TSIG Variables" of RFC 8945 §4.3.3: the TSIG fields that are
    ///     authenticated, leaving out the MAC itself (<see cref="DnsTsigOptions.Mac" />) and the
    ///     original ID (<see cref="DnsTsigOptions.OriginalId" />), which goes into the message bytes
    ///     instead (see <see cref="ExtractSignedMessageBytes" />). The key and algorithm names are
    ///     written in canonical form (RFC 4034 §6.2: lowercase, uncompressed), so a key named
    ///     <c>Ops.Key</c> produces the same MAC as <c>ops.key</c>.
    /// </summary>
    /// <param name="tsig">The TSIG fields to build the variables from.</param>
    /// <returns>The TSIG Variables, to append after the signed message bytes.</returns>
    public static byte[] BuildVariables(DnsTsigOptions tsig)
    {
        ArrayBufferWriter<byte> buffer = new();

        UncompressedDnsNameCodec.Write(buffer, tsig.KeyName.ToCanonicalLower());
        WriteUInt16(buffer, (ushort)DnsClassEnum.Any);
        WriteUInt32(buffer, 0);
        UncompressedDnsNameCodec.Write(buffer, tsig.AlgorithmName.ToCanonicalLower());
        WriteUInt48(buffer, tsig.TimeSigned);
        WriteUInt16(buffer, tsig.Fudge);
        WriteUInt16(buffer, (ushort)tsig.Error);
        WriteUInt16(buffer, (ushort)tsig.OtherData.Length);
        tsig.OtherData.CopyTo(buffer.GetSpan(tsig.OtherData.Length));
        buffer.Advance(tsig.OtherData.Length);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method builds the "TSIG Timers" of RFC 8945 §5.3.1: just the Time Signed and Fudge
    ///     fields. Every message after the first in a multi-message answer (usually an AXFR/IXFR) is
    ///     signed with these instead of the full <see cref="BuildVariables" />. Only the first
    ///     message needs to establish the key and algorithm; repeating them in every message of a
    ///     long transfer would add cost for nothing.
    /// </summary>
    /// <param name="timeSigned">The Time Signed field of the message being signed.</param>
    /// <param name="fudge">The Fudge field of the message being signed.</param>
    /// <returns>The TSIG Timers, to append after the prior MAC and message bytes.</returns>
    public static byte[] BuildTimersOnly(ulong timeSigned, ushort fudge)
    {
        ArrayBufferWriter<byte> buffer = new();

        WriteUInt48(buffer, timeSigned);
        WriteUInt16(buffer, fudge);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method rebuilds the "message" part of what a TSIG MAC covers (RFC 8945 §4.3.3,
    ///     "Msg'"): the bytes as received, up to but not including the TSIG record, with the
    ///     header's additional-record count reduced by one and the header ID replaced by the TSIG
    ///     Original ID. It works on the received bytes rather than re-serializing the parsed
    ///     <see cref="DnsMessage" />, because a different compression choice could produce bytes
    ///     that differ from what was signed. A forwarder that rewrote the header ID after signing
    ///     therefore does not break the MAC.
    /// </summary>
    /// <param name="rawMessage">The complete message, as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset where the TSIG record started, as reported by
    ///     <see cref="DnsMessageReader.Read(ReadOnlySpan{byte},out int)" />.
    /// </param>
    /// <param name="originalId">The Original ID from the TSIG record.</param>
    /// <returns>The message bytes that were signed.</returns>
    public static byte[] ExtractSignedMessageBytes(
        ReadOnlySpan<byte> rawMessage,
        int tsigRecordStartOffset,
        ushort originalId
    )
    {
        byte[] result = rawMessage[..tsigRecordStartOffset].ToArray();

        // The additional-record count lives at octets 10-11 of the 12-octet header.
        ushort additionalCount = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(10, 2));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10, 2), (ushort)(additionalCount - 1));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), originalId);

        return result;
    }

    /// <summary>
    ///     This method returns a copy of a message that is about to be signed, with the header ID
    ///     replaced by the TSIG Original ID, the sign-side counterpart of
    ///     <see cref="ExtractSignedMessageBytes" />. The additional-record count is untouched
    ///     because the message does not carry its TSIG record yet.
    /// </summary>
    /// <param name="unsignedMessage">The message as serialized, without a TSIG record.</param>
    /// <param name="originalId">The Original ID to record in the TSIG record.</param>
    /// <returns>The message bytes to digest.</returns>
    public static byte[] ApplyOriginalId(ReadOnlySpan<byte> unsignedMessage, ushort originalId)
    {
        byte[] result = unsignedMessage.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), originalId);

        return result;
    }

    /// <summary>
    ///     This method assembles the input of the HMAC for one TSIG message (RFC 8945 §4.3.3 and
    ///     §5.3.1). Signing and verification both call it, so they cannot disagree about the
    ///     construction. A chaining MAC is always written with its two-octet length first.
    /// </summary>
    /// <param name="kind">Which of the three digest shapes to build.</param>
    /// <param name="signedMessageBytes">
    ///     The message bytes (see <see cref="ExtractSignedMessageBytes" /> and
    ///     <see cref="ApplyOriginalId" />).
    /// </param>
    /// <param name="tsig">
    ///     The TSIG fields of the message, as received or as about to be sent. Only the key name,
    ///     algorithm, time, fudge, error and other data are digested.
    /// </param>
    /// <param name="chainMac">
    ///     The request MAC for <see cref="TsigDigestKindEnum.FirstResponse" /> or the prior MAC for
    ///     <see cref="TsigDigestKindEnum.Continuation" />; ignored for a request.
    /// </param>
    /// <param name="unsignedMessages">
    ///     The full bytes, as received, of every unsigned message since the prior signed one. Only
    ///     used for <see cref="TsigDigestKindEnum.Continuation" />; may be null or empty.
    /// </param>
    /// <returns>The bytes to feed into the HMAC.</returns>
    public static byte[] BuildDigestInput(
        TsigDigestKindEnum kind,
        ReadOnlySpan<byte> signedMessageBytes,
        DnsTsigOptions tsig,
        byte[] chainMac,
        IReadOnlyList<byte[]> unsignedMessages
    )
    {
        ArrayBufferWriter<byte> buffer = new();

        if (kind != TsigDigestKindEnum.Request)
        {
            chainMac ??= [];
            WriteUInt16(buffer, (ushort)chainMac.Length);
            buffer.Write(chainMac);
        }

        if (kind == TsigDigestKindEnum.Continuation && unsignedMessages is not null)
            foreach (byte[] unsigned in unsignedMessages)
                buffer.Write(unsigned);

        buffer.Write(signedMessageBytes);
        buffer.Write(
            kind == TsigDigestKindEnum.Continuation
                ? BuildTimersOnly(tsig.TimeSigned, tsig.Fudge)
                : BuildVariables(tsig)
        );

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method returns how many octets a TSIG record occupies on the wire, so a sender can
    ///     reserve room for it before the MAC exists (a UDP answer is truncated to leave space for
    ///     its signature). Both names are written uncompressed, as RFC 8945 §4.2 requires.
    /// </summary>
    /// <param name="keyName">The record owner, the key name.</param>
    /// <param name="algorithmName">The algorithm name in the RDATA.</param>
    /// <param name="macLength">The MAC length in octets.</param>
    /// <param name="otherLength">The Other Data length in octets.</param>
    public static int EstimateRecordLength(
        DnsName keyName,
        DnsName algorithmName,
        int macLength,
        int otherLength
    ) =>
        // Owner, TYPE/CLASS/TTL/RDLENGTH (10), algorithm, Time Signed (6), Fudge (2), MAC Size (2),
        // MAC, Original ID (2), Error (2), Other Len (2), Other Data.
        keyName.WireLength
        + 10
        + algorithmName.WireLength
        + 6
        + 2
        + 2
        + macLength
        + 2
        + 2
        + 2
        + otherLength;

    /// <summary>
    ///     This method writes a big-endian 16-bit value.
    /// </summary>
    private static void WriteUInt16(IBufferWriter<byte> buffer, ushort value)
    {
        Span<byte> span = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        buffer.Advance(2);
    }

    /// <summary>
    ///     This method writes a big-endian 32-bit value.
    /// </summary>
    private static void WriteUInt32(IBufferWriter<byte> buffer, uint value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        buffer.Advance(4);
    }

    /// <summary>
    ///     This method writes a big-endian 48-bit value, the width of the TSIG Time Signed field
    ///     (RFC 8945).
    /// </summary>
    private static void WriteUInt48(IBufferWriter<byte> buffer, ulong value)
    {
        Span<byte> span = buffer.GetSpan(6);

        for (int i = 0; i < 6; i++)
            span[i] = (byte)(value >> ((5 - i) * 8));

        buffer.Advance(6);
    }
}

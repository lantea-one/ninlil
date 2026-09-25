using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class builds the byte sequences that RFC 8945 §4.3.3 feeds into a TSIG message
///     authentication code. It only assembles wire bytes and does no cryptography. Computing and
///     verifying the MAC needs a shared secret and a key lookup, so it lives in
///     <c>Ninlil.Dns.Server.Tsig</c> instead.
/// </summary>
public static class TsigCodec
{
    /// <summary>
    ///     This method builds the "TSIG Variables" of RFC 8945 §4.3.3: the TSIG fields that are
    ///     authenticated, leaving out the MAC itself (<see cref="DnsTsigOptions.Mac" />) and the
    ///     original ID (<see cref="DnsTsigOptions.OriginalId" />), which the signed header already
    ///     covers.
    /// </summary>
    /// <param name="tsig">The TSIG fields to build the variables from.</param>
    /// <returns>The TSIG Variables, to append after the signed message bytes.</returns>
    public static byte[] BuildVariables(DnsTsigOptions tsig)
    {
        ArrayBufferWriter<byte> buffer = new();

        // The key and algorithm names are written in the case they were parsed with. Unlike
        // DNSSEC, RFC 8945 §4.3.3 does not lowercase the names in the TSIG Variables.
        UncompressedDnsNameCodec.Write(buffer, tsig.KeyName);
        WriteUInt16(buffer, (ushort)DnsClassEnum.Any);
        WriteUInt32(buffer, 0);
        UncompressedDnsNameCodec.Write(buffer, tsig.AlgorithmName);
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
    ///     This method rebuilds the "message" part of what a TSIG MAC covers (RFC 8945 §4.3.3): the
    ///     bytes as received, up to but not including the TSIG record, with the header's
    ///     additional-record count reduced by one. It works on the received bytes rather than
    ///     re-serializing the parsed <see cref="DnsMessage" />, because a different compression
    ///     choice could produce bytes that differ from what was signed.
    /// </summary>
    /// <param name="rawMessage">The complete message, as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset where the TSIG record started, as reported by
    ///     <see cref="DnsMessageReader.Read(ReadOnlySpan{byte},out int)" />.
    /// </param>
    /// <returns>The message bytes that were signed.</returns>
    public static byte[] ExtractSignedMessageBytes(
        ReadOnlySpan<byte> rawMessage,
        int tsigRecordStartOffset
    )
    {
        byte[] result = rawMessage[..tsigRecordStartOffset].ToArray();

        // The additional-record count lives at octets 10-11 of the 12-octet header.
        ushort additionalCount = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(10, 2));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10, 2), (ushort)(additionalCount - 1));

        return result;
    }

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

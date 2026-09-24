using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class builds the exact byte sequences RFC 8945 §4.3.3 defines as going into a TSIG
///     message authentication code -- pure, deterministic wire-format assembly with no
///     cryptography of its own. Computing and verifying an actual MAC from these bytes (which
///     needs a shared secret, and typically a key lookup) is deliberately kept out of
///     <c>Ninlil.Dns.Common</c>; see <c>Ninlil.Dns.Server.Tsig</c> for that.
/// </summary>
public static class TsigCodec
{
    /// <summary>
    ///     This method builds the "TSIG Variables" RFC 8945 §4.3.3 defines: everything about a
    ///     TSIG record relevant to its own authentication, excluding the fields that are either
    ///     the MAC itself (<see cref="DnsTsigOptions.Mac" />) or already implicitly covered by
    ///     the signed message's own header (<see cref="DnsTsigOptions.OriginalId" />).
    /// </summary>
    /// <param name="tsig">The TSIG fields to build the variables from.</param>
    /// <returns>The TSIG Variables, ready to be appended after the signed message's own bytes.</returns>
    public static byte[] BuildVariables(DnsTsigOptions tsig)
    {
        ArrayBufferWriter<byte> buffer = new();

        // The key name and algorithm name are written exactly as given, in whatever case they
        // were parsed with -- RFC 8945 §4.3.3 does not downcase or otherwise canonicalize the
        // domain names making up the TSIG Variables, unlike DNSSEC's canonical signing form.
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
    ///     This method builds the "TSIG Timers" RFC 8945 §5.3.1 defines: the reduced digest
    ///     component every second-and-subsequent message in a multi-message, TSIG-protected
    ///     answer sequence (a multi-message AXFR/IXFR, most commonly) is signed with, instead of
    ///     the full <see cref="BuildVariables" /> a first message or an ordinary single-message
    ///     answer uses -- just the Time Signed and Fudge fields, in that order, with none of the
    ///     key name, algorithm name, error, or other-data fields <see cref="BuildVariables" />
    ///     also includes. The RFC's own reasoning: signing every message in a long transfer with
    ///     the full variables would be needless, expensive overhead the connection itself does not
    ///     need, since only the very first message actually has to re-establish who is signing and
    ///     with what algorithm.
    /// </summary>
    /// <param name="timeSigned">The signing message's own Time Signed field.</param>
    /// <param name="fudge">The signing message's own Fudge field.</param>
    /// <returns>The TSIG Timers, ready to be appended after the running prior-MAC-plus-message bytes.</returns>
    public static byte[] BuildTimersOnly(ulong timeSigned, ushort fudge)
    {
        ArrayBufferWriter<byte> buffer = new();

        WriteUInt48(buffer, timeSigned);
        WriteUInt16(buffer, fudge);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method reconstructs the "message" component RFC 8945 §4.3.3 defines as the first
    ///     part of what a TSIG MAC covers: the raw bytes exactly as received, up to (but not
    ///     including) the TSIG record itself, with the header's additional-record count
    ///     decremented by one so it no longer counts the TSIG record that was removed. This
    ///     operates on the literal bytes a message arrived as, rather than re-serializing the
    ///     parsed <see cref="DnsMessage" />, since a different (but equally valid) choice of name
    ///     compression could otherwise produce different bytes than whatever was actually signed.
    /// </summary>
    /// <param name="rawMessage">The complete message, exactly as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset the TSIG record started at, as reported by
    ///     <see cref="DnsMessageReader" />'s message-decoding overload that reports the TSIG
    ///     record's starting offset.
    /// </param>
    /// <returns>The message bytes that were actually signed.</returns>
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
    ///     This method writes a big-endian, 48-bit value -- RFC 8945's TSIG Time Signed field is
    ///     48 bits wide rather than the usual 32 or 64, so it fits no standard integer type's
    ///     native wire width.
    /// </summary>
    private static void WriteUInt48(IBufferWriter<byte> buffer, ulong value)
    {
        Span<byte> span = buffer.GetSpan(6);

        for (int i = 0; i < 6; i++)
            span[i] = (byte)(value >> ((5 - i) * 8));

        buffer.Advance(6);
    }
}

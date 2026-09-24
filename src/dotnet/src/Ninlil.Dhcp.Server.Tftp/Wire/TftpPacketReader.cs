using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This class decodes the three TFTP packet shapes this server's own transfer loop ever
///     needs to read: a Read/Write Request (from the main listening socket), an Acknowledgment,
///     and an Error (both from a per-transfer socket, while a download is in progress). This
///     server never needs to decode a Data packet -- it only ever sends those, never receives
///     them, since it is read-only.
/// </summary>
public static class TftpPacketReader
{
    /// <summary>
    ///     This method attempts to decode a Read/Write Request packet.
    /// </summary>
    /// <param name="datagram">The raw datagram bytes to decode.</param>
    /// <param name="packet">The decoded packet, or <see langword="null" /> on failure.</param>
    /// <param name="failureReason">A human-readable reason decoding failed, or <see langword="null" /> on success.</param>
    /// <returns><see langword="true" /> if decoding succeeded.</returns>
    public static bool TryReadRequest(
        ReadOnlySpan<byte> datagram,
        out TftpRequestPacket packet,
        out string failureReason
    )
    {
        packet = null;

        if (!TryReadOpcode(datagram, out TftpOpcodeEnum opcode, out failureReason))
            return false;

        if (opcode is not (TftpOpcodeEnum.ReadRequest or TftpOpcodeEnum.WriteRequest))
        {
            failureReason = $"Opcode {(ushort)opcode} is not a Read or Write Request.";
            return false;
        }

        ReadOnlySpan<byte> remainder = datagram[2..];

        if (!TryReadNulTerminatedString(remainder, out string filename, out int consumed))
        {
            failureReason = "The request's own filename field is missing its terminating NUL.";
            return false;
        }

        remainder = remainder[consumed..];

        if (!TryReadNulTerminatedString(remainder, out string mode, out _))
        {
            failureReason = "The request's own mode field is missing its terminating NUL.";
            return false;
        }

        packet = new TftpRequestPacket(opcode, filename, mode);
        return true;
    }

    /// <summary>
    ///     This method attempts to decode an Acknowledgment packet's own block number.
    /// </summary>
    public static bool TryReadAcknowledgment(
        ReadOnlySpan<byte> datagram,
        out ushort blockNumber,
        out string failureReason
    )
    {
        blockNumber = 0;

        if (!TryReadOpcode(datagram, out TftpOpcodeEnum opcode, out failureReason))
            return false;

        if (opcode != TftpOpcodeEnum.Acknowledgment)
        {
            failureReason = $"Opcode {(ushort)opcode} is not an Acknowledgment.";
            return false;
        }

        if (datagram.Length < 4)
        {
            failureReason = "The acknowledgment is too short to carry a block number.";
            return false;
        }

        blockNumber = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..4]);
        return true;
    }

    /// <summary>
    ///     This method attempts to decode an Error packet's own code and message -- used only to
    ///     recognize a client-initiated transfer abort mid-transfer, never acted on beyond
    ///     logging it and stopping.
    /// </summary>
    public static bool TryReadError(
        ReadOnlySpan<byte> datagram,
        out TftpErrorCodeEnum errorCode,
        out string errorMessage,
        out string failureReason
    )
    {
        errorCode = TftpErrorCodeEnum.NotDefined;
        errorMessage = null;

        if (!TryReadOpcode(datagram, out TftpOpcodeEnum opcode, out failureReason))
            return false;

        if (opcode != TftpOpcodeEnum.Error)
        {
            failureReason = $"Opcode {(ushort)opcode} is not an Error.";
            return false;
        }

        if (datagram.Length < 4)
        {
            failureReason = "The error packet is too short to carry an error code.";
            return false;
        }

        errorCode = (TftpErrorCodeEnum)BinaryPrimitives.ReadUInt16BigEndian(datagram[2..4]);
        TryReadNulTerminatedString(datagram[4..], out errorMessage, out _);
        return true;
    }

    /// <summary>
    ///     This method reads the two-byte opcode every TFTP packet starts with.
    /// </summary>
    private static bool TryReadOpcode(
        ReadOnlySpan<byte> datagram,
        out TftpOpcodeEnum opcode,
        out string failureReason
    )
    {
        opcode = default;
        failureReason = null;

        if (datagram.Length < 2)
        {
            failureReason = "The datagram is too short to carry a TFTP opcode.";
            return false;
        }

        opcode = (TftpOpcodeEnum)BinaryPrimitives.ReadUInt16BigEndian(datagram[..2]);
        return true;
    }

    /// <summary>
    ///     This method reads a single NUL-terminated NVT ASCII string from the start of a span,
    ///     reporting how many bytes (including the terminating NUL) it consumed.
    /// </summary>
    private static bool TryReadNulTerminatedString(
        ReadOnlySpan<byte> span,
        out string value,
        out int consumed
    )
    {
        int nulIndex = span.IndexOf((byte)0);

        if (nulIndex < 0)
        {
            value = null;
            consumed = 0;
            return false;
        }

        value = Encoding.ASCII.GetString(span[..nulIndex]);
        consumed = nulIndex + 1;
        return true;
    }
}

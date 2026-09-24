using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This class encodes the two TFTP packet shapes this server ever sends as part of a normal
///     file transfer (Data, Acknowledgment is never sent by a server) plus Error -- this server
///     never needs to encode a Request packet, since it never originates one.
/// </summary>
public static class TftpPacketWriter
{
    /// <summary>
    ///     This method encodes a Data packet -- opcode, block number, and up to 512 bytes of
    ///     file content (RFC 1350 Section 5's own per-block size cap; a shorter final block, or
    ///     an empty one for a file whose length is an exact multiple of 512, signals end of
    ///     transfer).
    /// </summary>
    public static byte[] WriteData(ushort blockNumber, ReadOnlySpan<byte> blockData)
    {
        byte[] packet = new byte[4 + blockData.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), (ushort)TftpOpcodeEnum.Data);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), blockNumber);
        blockData.CopyTo(packet.AsSpan(4));
        return packet;
    }

    /// <summary>
    ///     This method encodes an Error packet.
    /// </summary>
    public static byte[] WriteError(TftpErrorCodeEnum errorCode, string errorMessage)
    {
        byte[] messageBytes = Encoding.ASCII.GetBytes(errorMessage ?? string.Empty);
        byte[] packet = new byte[4 + messageBytes.Length + 1];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), (ushort)TftpOpcodeEnum.Error);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)errorCode);
        messageBytes.CopyTo(packet.AsSpan(4));

        // The trailing NUL terminator is already present -- `new byte[]` zero-initializes, and
        // the array is exactly one byte longer than messageBytes for it.
        return packet;
    }
}

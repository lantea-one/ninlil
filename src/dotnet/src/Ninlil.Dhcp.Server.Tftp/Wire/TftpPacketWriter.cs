using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This class encodes the two TFTP packets a read-only server sends: Data and Error. It does
///     not encode requests or Acknowledgments, which only a client sends to this server.
/// </summary>
public static class TftpPacketWriter
{
    /// <summary>
    ///     This method encodes a Data packet: opcode, block number and the block content. The
    ///     caller keeps the content to 512 bytes; a shorter block, possibly empty, marks the end of
    ///     the transfer.
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
    ///     This method encodes an Error packet: opcode, error code and a NUL-terminated ASCII
    ///     message. Non-ASCII characters become <c>?</c>.
    /// </summary>
    public static byte[] WriteError(TftpErrorCodeEnum errorCode, string errorMessage)
    {
        byte[] messageBytes = Encoding.ASCII.GetBytes(errorMessage ?? string.Empty);
        byte[] packet = new byte[4 + messageBytes.Length + 1];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), (ushort)TftpOpcodeEnum.Error);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)errorCode);
        messageBytes.CopyTo(packet.AsSpan(4));

        // The NUL terminator is the array's last byte, which is already zero.
        return packet;
    }
}

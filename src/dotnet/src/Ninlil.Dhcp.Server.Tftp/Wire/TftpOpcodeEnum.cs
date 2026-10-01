// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This enumeration names the five RFC 1350 TFTP opcodes, the first two bytes (big-endian) of
///     every TFTP packet.
/// </summary>
public enum TftpOpcodeEnum : ushort
{
    /// <summary>A Read Request (RRQ): a client asking to download a file.</summary>
    ReadRequest = 1,

    /// <summary>
    ///     A Write Request (WRQ): a client asking to upload a file. The server is read-only and
    ///     always answers with an Access Violation error (see <see cref="TftpServer" />).
    /// </summary>
    WriteRequest = 2,

    /// <summary>A Data packet: one block of the file, at most 512 bytes.</summary>
    Data = 3,

    /// <summary>An Acknowledgment: confirms receipt of the Data block with the given block number.</summary>
    Acknowledgment = 4,

    /// <summary>An Error packet: reports why a request or transfer failed.</summary>
    Error = 5,
}

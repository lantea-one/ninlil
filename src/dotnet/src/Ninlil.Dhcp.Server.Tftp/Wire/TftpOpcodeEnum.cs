// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This enumeration names RFC 1350 Section 5's five TFTP packet opcodes -- the first two
///     bytes of every TFTP packet, on the wire in network byte order.
/// </summary>
public enum TftpOpcodeEnum : ushort
{
    /// <summary>A Read Request (RRQ) -- a client asking to download a file.</summary>
    ReadRequest = 1,

    /// <summary>
    ///     A Write Request (WRQ) -- a client asking to upload a file. This server always answers
    ///     one with an Access Violation error; see <see cref="TftpServer" />'s own doc comment
    ///     for why this server is deliberately read-only.
    /// </summary>
    WriteRequest = 2,

    /// <summary>A Data packet -- one block (at most 512 bytes) of a file's own content.</summary>
    Data = 3,

    /// <summary>An Acknowledgment -- confirms receipt of one Data block by its own block number.</summary>
    Acknowledgment = 4,

    /// <summary>An Error packet -- reports why a request or an in-progress transfer failed.</summary>
    Error = 5,
}

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This enumeration names the RFC 1350 TFTP error codes, carried in the two bytes after an
///     Error packet's opcode.
/// </summary>
public enum TftpErrorCodeEnum : ushort
{
    /// <summary>Not defined; see the error message text.</summary>
    NotDefined = 0,

    /// <summary>The requested file does not exist.</summary>
    FileNotFound = 1,

    /// <summary>
    ///     The operation is not permitted. The server sends this for every Write Request and for
    ///     a Read Request with an absolute path or a path outside the root directory.
    /// </summary>
    AccessViolation = 2,

    /// <summary>The disk is full, or the allocation has been exceeded.</summary>
    DiskFull = 3,

    /// <summary>
    ///     An illegal TFTP operation. The server sends this for a transfer mode other than octet
    ///     (see <see cref="TftpServer" />).
    /// </summary>
    IllegalOperation = 4,

    /// <summary>An unknown transfer ID: a packet arrived for a transfer the receiver does not know. The server does not send it.</summary>
    UnknownTransferId = 5,

    /// <summary>The requested file already exists (meaningful only for a Write Request).</summary>
    FileAlreadyExists = 6,

    /// <summary>No such user (only meaningful for mail mode, which the server does not support).</summary>
    NoSuchUser = 7,
}

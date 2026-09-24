// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This enumeration names RFC 1350 Section 5's TFTP error codes, carried by an Error packet's
///     own first two-byte field.
/// </summary>
public enum TftpErrorCodeEnum : ushort
{
    /// <summary>Not defined; see the error packet's own message text.</summary>
    NotDefined = 0,

    /// <summary>The requested file does not exist.</summary>
    FileNotFound = 1,

    /// <summary>
    ///     The requested operation is not permitted -- this server's own answer to every Write
    ///     Request, and to a Read Request naming a path outside its configured root directory.
    /// </summary>
    AccessViolation = 2,

    /// <summary>The disk is full, or the allocation has been exceeded.</summary>
    DiskFull = 3,

    /// <summary>
    ///     An illegal TFTP operation -- this server's own answer to a request naming an
    ///     unsupported transfer mode (see <see cref="TftpServer" />'s own doc comment for why
    ///     only octet mode is supported).
    /// </summary>
    IllegalOperation = 4,

    /// <summary>An unknown transfer ID -- a packet arrived for a transfer this server has no record of.</summary>
    UnknownTransferId = 5,

    /// <summary>The requested file already exists (meaningful only for a Write Request).</summary>
    FileAlreadyExists = 6,

    /// <summary>No such user (meaningful only for mail-mode transfers, which this server never supports).</summary>
    NoSuchUser = 7,
}

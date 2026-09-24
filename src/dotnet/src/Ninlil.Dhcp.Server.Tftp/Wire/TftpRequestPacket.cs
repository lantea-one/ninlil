// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This class represents a decoded RFC 1350 Section 5 Read Request (RRQ) or Write Request
///     (WRQ) packet -- the two packet types that share an identical wire shape (opcode, a
///     NUL-terminated filename, a NUL-terminated mode string), distinguished only by
///     <see cref="Opcode" />.
/// </summary>
/// <param name="Opcode">Which of the two request types this is.</param>
/// <param name="Filename">The file name the client named, exactly as sent (not yet sanitized).</param>
/// <param name="Mode">The transfer mode the client named (for example <c>"octet"</c>), exactly as sent.</param>
public record TftpRequestPacket(TftpOpcodeEnum Opcode, string Filename, string Mode);

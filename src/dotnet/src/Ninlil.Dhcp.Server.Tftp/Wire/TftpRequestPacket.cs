// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Wire;

/// <summary>
///     This record represents a decoded RFC 1350 Read Request (RRQ) or Write Request (WRQ). Both
///     have the same layout (opcode, NUL-terminated filename, NUL-terminated mode) and differ
///     only in <see cref="Opcode" />. Anything after the mode, such as RFC 2347 options, is
///     dropped.
/// </summary>
/// <param name="Opcode">Whether this is a read or a write request.</param>
/// <param name="Filename">The filename as sent, not yet checked.</param>
/// <param name="Mode">The transfer mode as sent, for example <c>"octet"</c>.</param>
public record TftpRequestPacket(TftpOpcodeEnum Opcode, string Filename, string Mode);

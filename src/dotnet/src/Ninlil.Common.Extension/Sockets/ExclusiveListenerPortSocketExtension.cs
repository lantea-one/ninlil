using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Common.Extension.Sockets;

/// <summary>
///     This static class makes a listening socket claim its port exclusively on Windows. Every
///     Ninlil socket that serves clients on a configured port (DNS over UDP and TCP, DNS over
///     TLS, DNSCrypt, DHCPv4, DHCPv6, TFTP and the Kestrel sockets of the web server and
///     DNS over HTTPS) calls it before <see cref="Socket.Bind(System.Net.EndPoint)" />, so no
///     other program can bind the same port on any address that overlaps the one Ninlil holds.
///     <para>
///         Windows lets a second socket bind a port that a first one holds when the two use
///         different address specificity (a wildcard and a specific address) or when either sets
///         <c>SO_REUSEADDR</c>. Windows then picks the receiving socket by address specificity, so
///         Ninlil would come up and silently miss the clients of the other socket. With exclusive
///         use the second bind fails instead, which Ninlil reports as a failed address. Microsoft
///         describes the options in "Using SO_REUSEADDR and SO_EXCLUSIVEADDRUSE" (Winsock
///         documentation). Linux and macOS refuse the second bind without help, and their
///         <c>SO_REUSEADDR</c> has other semantics, so nothing changes there.
///     </para>
/// </summary>
public static class ExclusiveListenerPortSocketExtension
{
    /// <summary>
    ///     This method describes why a listening socket could not bind, for the reason the health
    ///     check and the log show. On Windows an "address in use" or "access denied" error means
    ///     another program holds the port (Ninlil claims it exclusively), so the text says so and
    ///     names the <c>netstat</c> command that finds the program. In every other case it is the
    ///     exception's own message.
    /// </summary>
    /// <param name="exception">The bind exception.</param>
    /// <param name="protocolName">"UDP" or "TCP".</param>
    /// <param name="port">The port that was requested.</param>
    /// <returns>The reason text.</returns>
    public static string DescribeBindFailure(Exception exception, string protocolName, int port)
    {
        if (
            !OperatingSystem.IsWindows()
            || exception
                is not SocketException
                {
                    SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied,
                }
        )
            return exception.Message;

        return $"{exception.Message} On Windows Ninlil claims {protocolName} port {port} exclusively, so another program that holds that port on any overlapping address blocks it. Find it with 'netstat -ano | findstr :{port}'.";
    }

    /// <summary>
    ///     This extension method sets <see cref="Socket.ExclusiveAddressUse" /> on Windows and does
    ///     nothing elsewhere. Call it after creating the socket and before binding it. A socket
    ///     that claims its port this way must not also set <c>SO_REUSEADDR</c>: a caller that
    ///     wants reuse on other systems sets it only when this method returns <see langword="false" />.
    ///     Ports that Ninlil sockets must share by design (none today) keep reuse and skip this
    ///     call.
    /// </summary>
    /// <param name="socket">The listening socket, created and not yet bound.</param>
    /// <returns><see langword="true" /> when the socket now claims its port exclusively (Windows); <see langword="false" /> on every other system.</returns>
    public static bool ClaimPortExclusivelyOnWindows(this Socket socket)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        // Winsock refuses the option after bind with an error, and .NET throws
        // InvalidOperationException: callers set it first, so no exception is expected here.
        socket.ExclusiveAddressUse = true;
        return true;
    }
}

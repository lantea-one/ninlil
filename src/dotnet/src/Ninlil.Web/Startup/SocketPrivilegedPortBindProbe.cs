using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class implements <see cref="IPrivilegedPortBindProbe" /> by opening a throwaway UDP
///     socket, binding it to the wildcard address at the port and closing it again. That tests
///     this process's actual privilege rather than guessing from the port number and operating
///     system, since a container or systemd unit can give a non-root process Linux's
///     <c>CAP_NET_BIND_SERVICE</c>, letting it bind ports below 1024.
///     <para>
///         A UDP probe also answers for the TCP-based transports (Kestrel's HTTP and HTTPS,
///         DNS-over-TCP, DNS-over-TLS): the kernel's privileged-port rule, applied at
///         <c>bind()</c>, depends on the port number, not the protocol.
///     </para>
///     <para>
///         The probe socket is closed at once. The probes run one after another before any
///         listener has started (each binds only when the host starts its
///         <c>BackgroundService</c>), so a probe cannot collide with the listener that follows.
///     </para>
/// </summary>
public sealed class SocketPrivilegedPortBindProbe : IPrivilegedPortBindProbe
{
    /// <inheritdoc />
    public bool CanBind(int port)
    {
        // The privileged-port rule is a POSIX kernel rule (bind() fails with EACCES below 1024 for
        // an unprivileged process). Windows has no such rule at the socket level, so the port is
        // reported as bindable there to avoid a false warning.
        if (OperatingSystem.IsWindows())
            return true;

        try
        {
            using Socket probeSocket = new(
                AddressFamily.InterNetworkV6,
                SocketType.Dgram,
                ProtocolType.Udp
            );

            // Dual-stack, as the DNS listeners bind when listening on every address (see
            // ListenSocketSet), so the probe makes the same bind call they will.
            probeSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            probeSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

            return true;
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode == SocketError.AccessDenied)
        {
            // The one failure this probe looks for: the OS refused the bind for lack of privilege.
            return false;
        }
        catch (SocketException)
        {
            // Any other failure (the port is in use, the machine has no IPv6 stack and so on) is
            // real but not a privilege problem; the listener binding the port later reports it
            // itself. Treating it as bindable keeps this diagnostic from blaming privileges for an
            // unrelated problem.
            return true;
        }
    }
}

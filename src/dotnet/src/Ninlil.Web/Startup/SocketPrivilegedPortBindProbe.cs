using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class implements <see cref="IPrivilegedPortBindProbe" /> with a real, live check: it
///     actually opens a throwaway UDP socket, attempts to bind it to the wildcard address at the
///     given port, and immediately closes it again -- a genuine test of this exact process's own
///     current OS-level privilege, rather than a guess based on the port number and the
///     operating system alone (a container or a systemd unit can grant a non-root process Linux's
///     <c>CAP_NET_BIND_SERVICE</c> capability, in which case an ordinary, unprivileged-looking
///     process can legitimately bind a port below 1024).
///     <para>
///         A UDP probe is enough to answer this for every transport this project has, including
///         the TCP-based ones (Kestrel's own HTTP/HTTPS listener, DNS-over-TCP, DNS-over-TLS):
///         the reserved-port restriction this class is checking for (POSIX's privileged-port
///         rule, enforced by the kernel at <c>bind()</c> time) applies per port number, not per
///         transport-layer protocol, so a UDP bind failing or succeeding on a given port
///         accurately predicts whether a TCP bind on that same port would too.
///     </para>
///     <para>
///         Deliberately closes the probe socket immediately rather than holding it open -- this
///         runs once, sequentially, before any of this host's own real listeners have started
///         (each one binds its own socket only once the generic host actually starts that
///         listener's <c>BackgroundService</c>), so there is no real window in which this probe's
///         own temporary bind could collide with the real listener that follows it moments later.
///     </para>
/// </summary>
public sealed class SocketPrivilegedPortBindProbe : IPrivilegedPortBindProbe
{
    /// <inheritdoc />
    public bool CanBind(int port)
    {
        // The privileged-port restriction this probe exists to detect is a POSIX/Linux kernel
        // rule (bind() returning EACCES for an unprivileged process on a port below 1024).
        // Windows has no equivalent restriction at the raw socket API level -- any account can
        // ordinarily bind a low port there -- so treating this as "always bindable" on Windows
        // avoids a false-positive warning on a platform where the underlying condition this
        // check looks for does not exist in the first place.
        if (OperatingSystem.IsWindows())
            return true;

        try
        {
            using Socket probeSocket = new(
                AddressFamily.InterNetworkV6,
                SocketType.Dgram,
                ProtocolType.Udp
            );

            // Dual-stack, matching exactly how every real listener in this project (UdpDnsListener,
            // DnsOverTlsListener, DnsOverQuicListener, the DNSCrypt listeners) binds its own
            // socket, so this probe observes the identical bind call those listeners are about
            // to make moments later.
            probeSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            probeSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

            return true;
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode == SocketError.AccessDenied)
        {
            // This is the one specific failure this probe exists to detect: the OS refused the
            // bind purely because this process lacks the privilege to use this port at all.
            return false;
        }
        catch (SocketException)
        {
            // Any other failure (the port is already in use by something else entirely, this
            // machine has no IPv6 stack, and so on) is a real condition, but not the privilege
            // question this probe answers -- the real listener that binds this same port moments
            // later will surface that failure itself, on its own terms, exactly as it always has.
            // Treating it as "bindable" here avoids this diagnostic ever reporting a misleading
            // privilege warning for an unrelated problem.
            return true;
        }
    }
}

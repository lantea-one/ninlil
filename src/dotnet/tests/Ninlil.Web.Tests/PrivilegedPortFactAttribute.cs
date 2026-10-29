using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This attribute marks a test that binds a privileged port (below
///     <c>net.ipv4.ip_unprivileged_port_start</c>, 1024 by default on Linux). When the process may
///     not bind the port, the test is skipped with the reason and shows as skipped in the test
///     report, instead of returning early and reporting a pass that checked nothing.
///     <see cref="SocketPrivilegedPortBindProbe" />, the probe the host's startup diagnostics use,
///     decides. xUnit 2 has no runtime skip call, so the skip reason is set in the constructor,
///     which runs when the tests are discovered; the answer is the same for the whole run.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PrivilegedPortFactAttribute : FactAttribute
{
    /// <summary>
    ///     This constructor sets <see cref="FactAttribute.Skip" /> when the process cannot bind
    ///     the port.
    /// </summary>
    /// <param name="port">The privileged UDP port the test binds through the host.</param>
    public PrivilegedPortFactAttribute(int port)
    {
        if (!new SocketPrivilegedPortBindProbe().CanBind(port))
            Skip =
                $"Port {port} is privileged and this process may not bind it (see "
                + "net.ipv4.ip_unprivileged_port_start).";
    }
}

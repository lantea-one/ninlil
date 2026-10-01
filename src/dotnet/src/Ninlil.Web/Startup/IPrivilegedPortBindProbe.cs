// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This interface reports whether the running process can bind a given port.
///     <see cref="PrivilegedPortStartupDiagnostics" /> uses it rather than a socket call inline so
///     a unit test can supply a fixed answer instead of depending on the privileges of the
///     account running the test. <see cref="SocketPrivilegedPortBindProbe" /> is the real
///     implementation.
/// </summary>
public interface IPrivilegedPortBindProbe
{
    /// <summary>
    ///     This method reports whether this process can bind <paramref name="port" />.
    ///     <see langword="true" /> means the port is not privileged, or the process has the
    ///     privilege to bind it (root, or a grant such as Linux's <c>CAP_NET_BIND_SERVICE</c>);
    ///     <see langword="false" /> means a privileged port this process cannot bind.
    /// </summary>
    /// <param name="port">The port to test.</param>
    bool CanBind(int port);
}

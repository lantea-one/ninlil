// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This interface reports whether this running process can actually bind a given port right
///     now, extracted behind an interface (rather than a plain static socket call inline in
///     <see cref="PrivilegedPortStartupDiagnostics" />) purely so a unit test can supply a
///     deterministic, injected answer instead of depending on the real OS-level privilege state
///     of whatever account happens to be running the test -- the real, shipped
///     <see cref="SocketPrivilegedPortBindProbe" /> is what every non-test caller actually gets.
/// </summary>
public interface IPrivilegedPortBindProbe
{
    /// <summary>
    ///     This method reports whether this process can currently bind <paramref name="port" />.
    ///     A <see langword="true" /> result means either the port is not privileged at all, or
    ///     this process genuinely has the elevation (root, or an equivalent grant such as Linux's
    ///     <c>CAP_NET_BIND_SERVICE</c>) needed to bind it; <see langword="false" /> means a
    ///     privileged port this process cannot actually bind.
    /// </summary>
    /// <param name="port">The port to test.</param>
    bool CanBind(int port);
}

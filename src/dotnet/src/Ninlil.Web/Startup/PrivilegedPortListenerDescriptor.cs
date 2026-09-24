// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This record describes one real, currently-configured listener this host is about to start
///     -- a transport's own human-readable name plus the single TCP/UDP port number it is bound
///     to -- so <see cref="PrivilegedPortStartupDiagnostics" /> has something concrete to check
///     and to name in its own warning message. Deliberately holds nothing beyond that: this
///     project's actual <c>Options</c> classes (<c>WebListenOptions</c>,
///     <c>PlainProtocolOptions</c>, and so on) already carry every other real setting, and this
///     diagnostic only ever needs a port number and a label to report against.
/// </summary>
/// <param name="TransportName">
///     The human-readable name of the transport this listener belongs to, exactly as it should
///     appear in a startup warning (for example <c>"DNS-over-TLS"</c>).
/// </param>
/// <param name="Port">The TCP/UDP port this listener is configured to bind to.</param>
public sealed record PrivilegedPortListenerDescriptor(string TransportName, int Port);

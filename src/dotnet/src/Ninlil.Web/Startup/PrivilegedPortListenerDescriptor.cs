// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This record describes one configured listener the host is about to start, by transport
///     name and port, for <see cref="PrivilegedPortStartupDiagnostics" /> to check and name in its
///     warning. It holds nothing else; the options classes (<c>WebListenOptions</c>,
///     <c>PlainProtocolOptions</c> and so on) carry the other settings.
/// </summary>
/// <param name="TransportName">
///     The transport's name as it should appear in a startup warning (for example
///     <c>"DNS-over-TLS"</c>).
/// </param>
/// <param name="Port">The TCP/UDP port this listener is configured to bind to.</param>
public sealed record PrivilegedPortListenerDescriptor(string TransportName, int Port);

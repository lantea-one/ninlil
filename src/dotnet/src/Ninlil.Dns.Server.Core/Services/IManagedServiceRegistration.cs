// Define our namespace.
namespace Ninlil.Dns.Server.Core.Services;

/// <summary>
///     This interface describes a single long-running component of this server -- a DNS
///     transport listener or a background maintenance loop -- in a form a future REST controller
///     (Service Lifecycle Management's own <c>/api/services</c>, not built by this abstraction
///     itself) can start, stop, and query the running state of, live, mid-process, without
///     depending on the ASP.NET Core generic host ever calling <see cref="StartAsync" />/
///     <see cref="StopAsync" /> again on its own -- the host calls each of these exactly once,
///     at process startup and shutdown; a caller reaching this interface directly (through
///     whatever registers instances of it, added in a later phase) is what makes a genuine,
///     in-process restart possible at all.
///     <para>
///         Deliberately does not require an implementing component to be written against this
///         interface directly. Every existing transport/background service in this project is
///         already an <c>IHostedService</c> (most, a <c>BackgroundService</c>); every one of them
///         was individually audited for restart-safety (can <c>StartAsync</c> be called again,
///         cleanly, after a prior <c>StopAsync</c> completed, on the very same instance) as part
///         of the same pass that added this interface, and any that were not are already fixed. A
///         later phase wraps each one in <see cref="ManagedServiceRegistration" /> rather than
///         changing any of those ~24 classes, spread across a dozen different projects, to
///         implement this interface themselves.
///     </para>
/// </summary>
public interface IManagedServiceRegistration
{
    /// <summary>
    ///     This property contains the stable, operator-facing name this component is identified
    ///     by -- both in a future <c>GET /api/services</c> listing and in the URL segment a
    ///     future <c>POST /api/services/{name}/start</c>-shaped action addresses it by. Stable
    ///     across restarts of the whole process, not just of this one component.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     This property contains which of the two groups this component belongs to.
    /// </summary>
    ManagedServiceCategoryEnum Category { get; }

    /// <summary>
    ///     This property reports whether this component is currently considered running. Exactly
    ///     what "running" means is this registration's own choice: a component with no richer
    ///     self-reported signal of its own falls back to "has <see cref="StartAsync" /> been
    ///     called more recently than <see cref="StopAsync" />," while a component that already
    ///     exposes a more accurate, independently-observed signal (a DNS transport listener's own
    ///     <c>IsListening</c>, for example, which can legitimately stay <see langword="false" />
    ///     even after a successful <see cref="StartAsync" /> call, for a transport that is enabled
    ///     but deliberately declined to bind -- a blank DNSCrypt provider name, an unsupported
    ///     platform for DNS-over-QUIC) reports that instead.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     This method starts this component -- binding a transport's own socket, or resuming a
    ///     background loop -- right now, in the running process. Calling this on a component that
    ///     is already running is this registration's own choice of how to behave (most components
    ///     wrapped by <see cref="ManagedServiceRegistration" /> simply call the underlying
    ///     component's own <c>StartAsync</c> again, which is safe only because this project's own
    ///     restart-safety audit already confirmed it is).
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     This method stops this component -- unbinding a transport's own socket, or halting a
    ///     background loop -- right now, in the running process, leaving every other component
    ///     this server runs untouched.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task StopAsync(CancellationToken cancellationToken);
}

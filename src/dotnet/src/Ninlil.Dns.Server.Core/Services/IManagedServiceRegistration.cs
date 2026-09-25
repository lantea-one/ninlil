// Define our namespace.
namespace Ninlil.Dns.Server.Core.Services;

/// <summary>
///     This interface describes one long-running component of the server (a DNS transport
///     listener or a background loop) so it can be started, stopped and checked while the
///     process runs. <c>Ninlil.Web.Services.ManagedServiceRegistry</c> holds the registrations
///     and the <c>/api/services</c> endpoints use them. The host calls a component's
///     <c>StartAsync</c> and <c>StopAsync</c> only at startup and shutdown; calling them through
///     this interface is what allows a restart in between.
///     <para>
///         Components do not implement this themselves. They are ordinary
///         <c>IHostedService</c>s wrapped in <see cref="ManagedServiceRegistration" />, and each
///         must tolerate <c>StartAsync</c> being called again on the same instance after
///         <c>StopAsync</c>.
///     </para>
/// </summary>
public interface IManagedServiceRegistration
{
    /// <summary>
    ///     This property contains the stable name the component is known by, both in the
    ///     <c>GET /api/services</c> listing and in URLs such as
    ///     <c>POST /api/services/{name}/restart</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     This property contains which of the two groups this component belongs to.
    /// </summary>
    ManagedServiceCategoryEnum Category { get; }

    /// <summary>
    ///     This property reports whether the component is running. A component with its own
    ///     signal reports that (a transport's <c>IsListening</c>, which stays
    ///     <see langword="false" /> when an enabled transport declines to bind, such as DNSCrypt
    ///     with no provider name or DNS-over-QUIC without platform support). Others report whether
    ///     <see cref="StartAsync" /> ran more recently than <see cref="StopAsync" />.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     This method starts the component now, binding a transport's socket or resuming a
    ///     background loop. What happens if it is already running is up to the implementation;
    ///     <see cref="ManagedServiceRegistration" /> calls the component's <c>StartAsync</c> again.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     This method stops the component now, closing a transport's socket or halting a
    ///     background loop, without affecting other components.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task StopAsync(CancellationToken cancellationToken);
}

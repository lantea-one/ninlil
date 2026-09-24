using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Services;

/// <summary>
///     This class implements <see cref="IManagedServiceRegistration" /> by wrapping an existing
///     <see cref="IHostedService" /> instance -- the shape every DNS transport listener and every
///     background maintenance loop in this project already is -- rather than requiring any of
///     them to be rewritten against a new interface directly. A later phase constructs one of
///     these per transport/background service it wants to expose through
///     <c>/api/services</c>, giving it a name and category the underlying component itself knows
///     nothing about, and registers the result somewhere that REST controller can enumerate.
///     <para>
///         <see cref="StartAsync" />/<see cref="StopAsync" /> simply call straight through to the
///         wrapped instance's own identically-named methods -- calling them a second, third, or
///         Nth time, mid-process, only works because every wrapped instance was already confirmed
///         restart-safe by this project's own dedicated audit (including the
///         fixes a few components needed to actually get there: a query-processing channel and a
///         message-broker consumer's own subscription that were previously torn down once and
///         never rebuilt, and two TLS-based listeners that leaked a certificate-reload timer on
///         every restart). This class performs no restart-safety work of its own -- it is a thin
///         adapter, not a safety net.
///     </para>
/// </summary>
public sealed class ManagedServiceRegistration : IManagedServiceRegistration
{
    /// <summary>
    ///     This property contains the wrapped component this registration's own
    ///     <see cref="StartAsync" />/<see cref="StopAsync" /> calls are forwarded to.
    /// </summary>
    private readonly IHostedService _service;

    /// <summary>
    ///     This property contains an optional, richer probe of the wrapped component's own actual
    ///     running state -- when supplied, this is what <see cref="IsRunning" /> reports instead
    ///     of this registration's own "has <see cref="StartAsync" /> been called more recently
    ///     than <see cref="StopAsync" />" bookkeeping. Most useful for a component (a DNS
    ///     transport listener, most commonly) that can legitimately still be not-actually-running
    ///     after a successful <see cref="StartAsync" /> call -- a transport that is enabled in
    ///     configuration but declined to bind for a reason of its own (a blank DNSCrypt provider
    ///     name, DNS-over-QUIC on a platform with no <c>libmsquic</c>).
    /// </summary>
    private readonly Func<bool> _isRunningProbe;

    /// <summary>
    ///     This property tracks this registration's own fallback notion of "running," used only
    ///     when no <see cref="_isRunningProbe" /> was supplied: <see langword="true" /> once
    ///     <see cref="StartAsync" /> has completed, <see langword="false" /> once
    ///     <see cref="StopAsync" /> has completed. Marked <see langword="volatile" /> since a
    ///     future REST controller's own <c>GET /api/services</c> handler reads
    ///     <see cref="IsRunning" /> from a different request than whichever one most recently
    ///     called <see cref="StartAsync" />/<see cref="StopAsync" />.
    /// </summary>
    private volatile bool _startedNotYetStopped;

    /// <summary>
    ///     This constructor wraps an existing <see cref="IHostedService" /> instance, giving it
    ///     the stable name and category <see cref="IManagedServiceRegistration" /> requires.
    /// </summary>
    /// <param name="name">This registration's own stable, operator-facing name.</param>
    /// <param name="category">Which of the two groups this component belongs to.</param>
    /// <param name="service">The existing component to wrap.</param>
    /// <param name="isRunningProbe">
    ///     An optional, richer probe of the wrapped component's own actual running state -- see
    ///     <see cref="_isRunningProbe" />'s own doc comment. Omitted (the default) for a component
    ///     with no such signal of its own, falling back to this registration's own start/stop
    ///     bookkeeping instead.
    /// </param>
    /// <param name="isAlreadyRunning">
    ///     Whether the wrapped <paramref name="service" /> has already been started by something
    ///     other than this registration itself before this constructor ever runs -- seeds this
    ///     registration's own start/stop bookkeeping fallback so <see cref="IsRunning" />
    ///     correctly reports <see langword="true" /> immediately, without needing a redundant
    ///     (and, for a component that does not tolerate a concurrent double-start, actively
    ///     dangerous) extra <see cref="StartAsync" /> call through this wrapper first. This is not
    ///     a hypothetical: every real service this project's own composition root wraps was
    ///     already started once by the ASP.NET Core generic host's own automatic
    ///     <see cref="IHostedService.StartAsync" /> call at process startup, well before any
    ///     registry is ever built to wrap it, so that composition root always passes
    ///     <see langword="true" /> here. Defaults to <see langword="false" />, the correct posture
    ///     for a registration built around a component that genuinely has not started yet (a fresh
    ///     unit test's own fake service, most commonly).
    /// </param>
    public ManagedServiceRegistration(
        string name,
        ManagedServiceCategoryEnum category,
        IHostedService service,
        Func<bool> isRunningProbe = null,
        bool isAlreadyRunning = false
    )
    {
        Name = name;
        Category = category;
        _service = service;
        _isRunningProbe = isRunningProbe;
        _startedNotYetStopped = isAlreadyRunning;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public ManagedServiceCategoryEnum Category { get; }

    /// <inheritdoc />
    public bool IsRunning => _isRunningProbe?.Invoke() ?? _startedNotYetStopped;

    /// <summary>
    ///     This constant bounds how long <see cref="StartAsync" /> waits for <see cref="_isRunningProbe" />
    ///     to actually turn <see langword="true" /> after the wrapped component's own
    ///     <see cref="IHostedService.StartAsync" /> call returns -- see that method's own doc
    ///     comment for why this wait exists at all. Two seconds is generous next to how quickly a
    ///     real socket bind actually completes in practice, while still bounded so a caller of
    ///     <see cref="StartAsync" /> (a REST action, most commonly) never hangs indefinitely on a
    ///     component that genuinely never reports itself running (a transport that is enabled in
    ///     configuration but declined to bind for a reason of its own).
    /// </summary>
    private static readonly TimeSpan StartupProbePollTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     This constant contains how often <see cref="StartAsync" /> re-checks
    ///     <see cref="_isRunningProbe" /> while waiting for it to turn <see langword="true" />.
    /// </summary>
    private static readonly TimeSpan StartupProbePollInterval = TimeSpan.FromMilliseconds(10);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _service.StartAsync(cancellationToken);
        _startedNotYetStopped = true;

        // .NET's own BackgroundService.StartAsync -- the base class every real component this
        // wraps other than DnsQueryProcessingPipeline/AuthoritativeZoneCache/SignedZoneCache
        // ultimately derives from -- does not run ExecuteAsync's own synchronous prefix (a
        // transport listener's own socket bind, most commonly) inline before returning: it
        // schedules ExecuteAsync to actually start running and returns immediately, confirmed
        // empirically via a throwaway probe rather than assumed, since this is exactly the kind
        // of internal-implementation-detail question a doc comment does not reliably answer.
        // Without this wait, a caller reading IsRunning immediately after StartAsync completes
        // (a REST action returning the result of the start it just performed, most commonly)
        // would see a stale "not yet running" value for any component with a real probe of its
        // own, even though the real start reliably finishes a few milliseconds later -- exactly
        // the gap a genuine hot-restart REST endpoint cannot afford to report incorrectly. A
        // component with no richer probe (this registration's own bookkeeping fallback) has no
        // such gap at all -- _startedNotYetStopped above is already set the moment this method's
        // own caller-visible work is done -- so this wait is skipped entirely in that case.
        if (_isRunningProbe is not null)
        {
            DateTime deadline = DateTime.UtcNow + StartupProbePollTimeout;

            while (!_isRunningProbe() && DateTime.UtcNow < deadline)
                await Task.Delay(StartupProbePollInterval, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _service.StopAsync(cancellationToken);
        _startedNotYetStopped = false;
    }
}

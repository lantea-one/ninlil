using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Services;

/// <summary>
///     This class implements <see cref="IManagedServiceRegistration" /> by wrapping an existing
///     <see cref="IHostedService" />, giving it a name and category the component itself does
///     not know. <c>Ninlil.Web.Services.ManagedServiceRegistry</c> builds one per transport and
///     background service.
///     <para>
///         <see cref="StartAsync" /> and <see cref="StopAsync" /> call straight through to the
///         wrapped service. Restarting works only because each wrapped service rebuilds what its
///         previous stop tore down (its channel, broker subscription, timers). This class adds no
///         restart safety of its own.
///     </para>
/// </summary>
public sealed class ManagedServiceRegistration : IManagedServiceRegistration
{
    /// <summary>
    ///     This field contains the wrapped component that start and stop calls go to.
    /// </summary>
    private readonly IHostedService _service;

    /// <summary>
    ///     This field contains an optional probe of whether the component is really running. When
    ///     set, <see cref="IsRunning" /> reports it instead of the start/stop bookkeeping. It suits
    ///     a transport that can stay down after a successful start, such as DNSCrypt with no
    ///     provider name or DNS-over-QUIC without <c>libmsquic</c>.
    /// </summary>
    private readonly Func<bool> _isRunningProbe;

    /// <summary>
    ///     This field is the fallback running state when there is no <see cref="_isRunningProbe" />:
    ///     <see langword="true" /> after <see cref="StartAsync" /> completes,
    ///     <see langword="false" /> after <see cref="StopAsync" /> completes. It is volatile
    ///     because a different request thread reads it.
    /// </summary>
    private volatile bool _startedNotYetStopped;

    /// <summary>
    ///     This field is the gate that lets only one <see cref="StartAsync" /> or
    ///     <see cref="StopAsync" /> run at a time.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     This constructor wraps an existing <see cref="IHostedService" /> with a name and
    ///     category.
    /// </summary>
    /// <param name="name">The stable name the component is known by.</param>
    /// <param name="category">Which of the two groups this component belongs to.</param>
    /// <param name="service">The existing component to wrap.</param>
    /// <param name="isRunningProbe">
    ///     An optional probe of whether the component is running (see <see cref="_isRunningProbe" />).
    ///     Without it, the start/stop bookkeeping is used.
    /// </param>
    /// <param name="isAlreadyRunning">
    ///     Whether something else, usually the host at startup, has already started
    ///     <paramref name="service" />. It seeds the fallback running state so
    ///     <see cref="IsRunning" /> is correct without a second start. The web host always passes
    ///     <see langword="true" />, because the host starts every hosted service before the
    ///     registry is built. Tests with a fresh fake service leave it <see langword="false" />.
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
    ///     This field contains how long <see cref="StartAsync" /> waits for
    ///     <see cref="_isRunningProbe" /> to become <see langword="true" /> (see the comment in
    ///     that method). A socket bind takes far less, and the bound keeps a REST call from
    ///     hanging on a transport that declined to bind.
    /// </summary>
    private static readonly TimeSpan StartupProbePollTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     This field contains how often <see cref="StartAsync" /> re-checks
    ///     <see cref="_isRunningProbe" /> while waiting for it to turn <see langword="true" />.
    /// </summary>
    private static readonly TimeSpan StartupProbePollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    ///     This method starts the wrapped service unless it is already running. A service that was
    ///     started but reports down is stopped first, so the wrapped service never has two
    ///     <c>ExecuteAsync</c> loops at once.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_startedNotYetStopped)
            {
                if (IsRunning)
                    return;

                // Started, but the probe says it is down: stop it before starting again.
                await _service.StopAsync(cancellationToken);
                _startedNotYetStopped = false;
            }

            await StartLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     This method starts the wrapped service and waits for the probe. The caller holds the
    ///     gate.
    /// </summary>
    private async Task StartLockedAsync(CancellationToken cancellationToken)
    {
        await _service.StartAsync(cancellationToken);
        _startedNotYetStopped = true;

        // BackgroundService.StartAsync returns before ExecuteAsync has run far enough to bind a
        // socket, so a probe read right away would still say "not running". Poll the probe
        // briefly so a REST restart reports the real state. Without a probe there is nothing
        // to wait for.
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
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await _service.StopAsync(cancellationToken);
            _startedNotYetStopped = false;
        }
        finally
        {
            _gate.Release();
        }
    }
}

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class lets one DHCP lease DNS registration run at a time in this process.
///     <see cref="DhcpLeaseDnsRegistrationEventSubscriber" /> waits on it around each handling.
///     Every lease write publishes its event after its commit, so the handling of the last write
///     starts after that commit and reads the final state of the row; handlings never overlap, so
///     an earlier handling that read an older state has finished writing before a later one
///     reads. It also closes the race between two grants of one host name, which the owner-name
///     checks of <see cref="DhcpDnsRegistrationService" /> cannot see across two handlings. Lease
///     events come once per DHCP exchange, not per query, so waiting costs little. It is a
///     singleton, registered by <c>AddNinlilDhcpDnsIntegration</c>.
/// </summary>
public sealed class DhcpDnsRegistrationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    ///     This method waits until no other registration is running, then takes the gate. The
    ///     caller must call <see cref="Release" /> exactly once afterwards.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the wait.</param>
    public Task WaitAsync(CancellationToken stoppingToken) => _semaphore.WaitAsync(stoppingToken);

    /// <summary>
    ///     This method gives the gate back so the next waiting registration can run.
    /// </summary>
    public void Release() => _semaphore.Release();
}

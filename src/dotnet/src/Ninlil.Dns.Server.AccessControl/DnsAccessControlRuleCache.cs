using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This class holds every enabled <see cref="DnsAccessControlRuleEntity" /> in memory, so
///     <see cref="DnsAccessControlEngine" /> can answer capability checks on the query path
///     without a database round trip.
///     <para>
///         It follows the pattern of <c>Ninlil.Dns.Server.Authoritative.Cache.AuthoritativeZoneCache</c>:
///         it loads in <see cref="StartAsync" /> and is invalidated through the event bus
///         (<see cref="DnsAccessControlRuleChangedEventSubscriber" />, fed by
///         <c>DnsAccessControlRulesController</c>). Unlike the zone cache, which reloads one
///         changed zone, it always reloads the whole rule set: the table is small and flat, so a
///         full reload is simple and cheap.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach <see cref="IDnsAccessControlRuleRepository" /> in a short-lived
///     scope, so the cache never holds a database context open longer than one load.
/// </param>
public sealed class DnsAccessControlRuleCache(IServiceScopeFactory scopeFactory) : IHostedService
{
    /// <summary>
    ///     This field contains the current snapshot of every enabled rule. A reload replaces the
    ///     whole list and never mutates it in place, so a concurrent reader never sees a
    ///     half-built snapshot.
    /// </summary>
    private volatile IReadOnlyList<DnsAccessControlRuleEntity> _rules = [];

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await ReloadAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method checks whether any cached rule grants a capability to an address. It reads
    ///     only the in-memory snapshot in <see cref="_rules" />, never the database.
    /// </summary>
    /// <param name="remoteAddress">The querying client's source address.</param>
    /// <param name="capability">The capability being checked for.</param>
    /// <returns><see langword="true" /> if at least one cached rule grants it.</returns>
    public bool HasCapability(IPAddress remoteAddress, DnsAccessControlCapabilityEnum capability)
    {
        // Read the snapshot reference once: ReloadAsync may swap _rules concurrently, and one
        // check must see a single consistent list, never a mix of old and new.
        IReadOnlyList<DnsAccessControlRuleEntity> rules = _rules;

        foreach (DnsAccessControlRuleEntity rule in rules)
        {
            if (!rule.Capabilities.HasFlag(capability))
                continue;

            if (ClientAddress.NetworkContains(rule.Cidr, remoteAddress))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     This method reloads the whole snapshot from the database and replaces the current one.
    ///     There is no single-rule reload (see the class summary for why).
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReloadAsync(CancellationToken stoppingToken = default)
    {
        // A plain scope reads the primary. The snapshot is reloaded at startup and after a rule
        // change event, never on a timer, so a replica may not have the change yet. Do not use
        // CreateReplicaReadScope here.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();

        _rules = await repository.ListEnabledAsync(stoppingToken);
    }
}

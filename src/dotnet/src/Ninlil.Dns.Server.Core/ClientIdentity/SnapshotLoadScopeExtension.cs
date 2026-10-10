using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Routing;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This class holds the one rule every <see cref="RefreshingSnapshot{T}" /> loader that reads a
///     database follows to open its scope: a load that only follows the age of the value
///     (<see cref="SnapshotLoadReasonEnum.TimeToLive" />) reads through
///     <see cref="ServiceScopeFactoryReadExtension.CreateReplicaReadScope" />, which uses a read
///     replica when one is configured and eligible; every other load uses a plain scope, which
///     is the primary. Without a replica both are the same database.
/// </summary>
public static class SnapshotLoadScopeExtension
{
    /// <summary>
    ///     This method opens the scope a snapshot load reads from.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceScopeFactory" /> instance.</param>
    /// <param name="reason">Why the snapshot loads.</param>
    /// <returns>The new scope; dispose it when the load is done.</returns>
    public static AsyncServiceScope CreateLoadScope(
        this IServiceScopeFactory instance,
        SnapshotLoadReasonEnum reason
    ) =>
        reason == SnapshotLoadReasonEnum.TimeToLive
            ? instance.CreateReplicaReadScope()
            : instance.CreateAsyncScope();
}

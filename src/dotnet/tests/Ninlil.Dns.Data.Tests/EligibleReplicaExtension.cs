using Ninlil.Dns.Data.Abstractions.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     This class holds the test helper that stands in for the replica health monitor: the router
///     sends nothing to a replica until a health check has passed, so tests that need a replica
///     read enable the router and report a passing check in one call.
/// </summary>
internal static class EligibleReplicaExtension
{
    /// <summary>
    ///     This method enables <paramref name="router" /> and reports a replica that is current
    ///     and has no lag.
    /// </summary>
    /// <param name="router">The router under test.</param>
    public static void EnableWithAnEligibleReplica(this IStorageReadRouter router)
    {
        router.Enable();
        router.ReportReplicaHealth(
            new ReplicaHealthStatus(true, 0, true, DateTimeOffset.UtcNow, "")
        );
    }
}

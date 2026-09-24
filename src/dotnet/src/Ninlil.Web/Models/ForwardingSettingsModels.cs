using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>ForwardingOptions</c>. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts. Forwarder entries
///     themselves (the actual upstream addresses) are a separate resource, already fully
///     CRUD-able via <c>ForwardersController</c> -- this record covers only the engine-level
///     toggle and failover/health-check tuning that applies across every configured forwarder.
/// </summary>
public record ForwardingSettingsDetail(
    bool Enable,
    ForwarderFailoverStrategyEnum FailoverStrategy,
    TimeSpan HealthCheckInterval,
    TimeSpan PerForwarderTimeout
);

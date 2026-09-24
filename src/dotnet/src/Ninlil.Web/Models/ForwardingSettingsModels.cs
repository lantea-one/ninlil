using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>ForwardingOptions</c>. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts. Forwarder entries
///     themselves (the actual upstream addresses) are a separate resource, already fully
///     CRUD-able via <c>ForwardersController</c> -- this record covers only the engine-level
///     toggle, failover/health-check tuning and forwarded-answer cache settings that apply across
///     every configured forwarder.
/// </summary>
/// <param name="Enable">Whether configured forwarders are consulted at all.</param>
/// <param name="FailoverStrategy">How one forwarder is picked among several.</param>
/// <param name="HealthCheckInterval">How often each forwarder's health is probed.</param>
/// <param name="PerForwarderTimeout">How long one forwarder may take before the next is tried.</param>
/// <param name="EnableCache">Whether forwarded answers are cached.</param>
/// <param name="MaxCacheEntries">How many forwarded answers may be cached before the soonest-to-expire are evicted.</param>
public record ForwardingSettingsDetail(
    bool Enable,
    ForwarderFailoverStrategyEnum FailoverStrategy,
    TimeSpan HealthCheckInterval,
    TimeSpan PerForwarderTimeout,
    bool EnableCache,
    int MaxCacheEntries
);

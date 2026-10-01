using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the forwarding engine settings (<c>ForwardingOptions</c>)
///     that <c>GET /api/forwarding-settings</c> returns. Nothing in it is secret, so a
///     <c>PATCH</c> to the same endpoint accepts the same fields. The forwarders themselves are a
///     separate resource (<c>ForwardersController</c>); this record holds the on/off switch,
///     failover and health-check tuning, and the forwarded-answer cache settings that apply to
///     all of them.
/// </summary>
/// <param name="Enable">Whether the configured forwarders are used at all. Defaults to <see langword="false" />.</param>
/// <param name="FailoverStrategy">How one forwarder is picked among several. Defaults to health-checked round robin.</param>
/// <param name="HealthCheckInterval">How often each forwarder's health is probed under health-checked round robin. Defaults to 30 seconds.</param>
/// <param name="PerForwarderTimeout">How long one forwarder may take to answer before the attempt counts as failed. Defaults to 3 seconds.</param>
/// <param name="EnableCache">Whether forwarded answers are cached. Defaults to <see langword="true" />.</param>
/// <param name="MaxCacheEntries">How many forwarded answers may be cached before the soonest to expire are evicted. Defaults to 10000.</param>
public record ForwardingSettingsDetail(
    bool Enable,
    ForwarderFailoverStrategyEnum FailoverStrategy,
    TimeSpan HealthCheckInterval,
    TimeSpan PerForwarderTimeout,
    bool EnableCache,
    int MaxCacheEntries
);

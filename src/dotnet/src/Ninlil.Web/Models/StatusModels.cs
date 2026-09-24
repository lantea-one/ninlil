// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a single response for <c>GET /status</c> -- an unauthenticated caller
///     (or one without the <c>status:read</c> permission claim) only ever receives
///     <see cref="Status" /> populated, with <see cref="Components" /> left <see langword="null" />;
///     an authenticated caller holding <c>status:read</c> receives both, additively --
///     the granular per-component breakdown is the single source of truth
///     <see cref="Status" /> itself is aggregated down from, not a separately maintained summary.
/// </summary>
/// <param name="Status">The overall aggregate status: <c>Healthy</c>, <c>Degraded</c>, or <c>Unhealthy</c>.</param>
/// <param name="Components">The per-subsystem breakdown, present only for an authenticated caller with <c>status:read</c>.</param>
public record StatusResponse(string Status, IReadOnlyList<StatusComponent> Components);

/// <summary>
///     This class carries a single subsystem's own health-check result, part of an authenticated
///     <see cref="StatusResponse.Components" /> response.
/// </summary>
/// <param name="Name">The health check's own registered name (for example, <c>storage</c>, <c>transports</c>).</param>
/// <param name="Status">This one component's own status: <c>Healthy</c>, <c>Degraded</c>, or <c>Unhealthy</c>.</param>
/// <param name="Description">A human-readable description of this component's own current state.</param>
/// <param name="Error">The message of an exception this check raised, if any, or <see langword="null" />.</param>
public record StatusComponent(string Name, string Status, string Description, string Error);

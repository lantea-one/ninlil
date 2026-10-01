// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the response of <c>GET /status</c>. A caller without the
///     <c>status:read</c> permission, including an anonymous one, gets only <see cref="Status" />,
///     with <see cref="Components" /> <see langword="null" />. A caller with it gets both.
///     <see cref="Status" /> is aggregated from the same health checks the components report.
/// </summary>
/// <param name="Status">The overall status: <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.</param>
/// <param name="Components">The per-subsystem breakdown, present only for an authenticated caller with <c>status:read</c>.</param>
public record StatusResponse(string Status, IReadOnlyList<StatusComponent> Components);

/// <summary>
///     This class carries one health check's result, as listed in
///     <see cref="StatusResponse.Components" />.
/// </summary>
/// <param name="Name">The health check's registered name (for example <c>storage</c> or <c>transports</c>).</param>
/// <param name="Status">The component's status: <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.</param>
/// <param name="Description">A human-readable description of the component's state.</param>
/// <param name="Error">The message of an exception the check raised, or <see langword="null" />.</param>
public record StatusComponent(string Name, string Status, string Description, string Error);

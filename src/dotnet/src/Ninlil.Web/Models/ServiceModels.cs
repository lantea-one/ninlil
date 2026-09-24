using Ninlil.Dns.Server.Core.Services;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of one <see cref="IManagedServiceRegistration" /> --
///     a DNS transport listener or background maintenance loop this server runs, and whether it
///     is genuinely running right now.
/// </summary>
/// <param name="Name">
///     This service's own stable, operator-facing name -- the exact value a
///     <c>POST /api/services/{name}/start</c>-shaped action's own route parameter addresses it by.
/// </param>
/// <param name="Category">Which of the two groups this service belongs to.</param>
/// <param name="IsRunning">
///     Whether this service is genuinely running right now -- see
///     <see cref="IManagedServiceRegistration.IsRunning" />'s own doc comment for exactly what
///     that means for a given service (a real, independently-observed signal for every DNS
///     transport listener; "has start been called more recently than stop" for a background
///     maintenance loop with no richer signal of its own).
/// </param>
public record ManagedServiceSummary(
    string Name,
    ManagedServiceCategoryEnum Category,
    bool IsRunning
);

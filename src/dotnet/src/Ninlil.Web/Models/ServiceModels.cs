using Ninlil.Dns.Server.Core.Services;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record describes one <see cref="IManagedServiceRegistration" />, as listed by
///     <c>GET /api/services</c>: a DNS transport listener or background maintenance loop, and
///     whether it is running.
/// </summary>
/// <param name="Name">
///     The service's stable name, used as <c>{name}</c> in <c>POST /api/services/{name}/start</c>
///     and the other control actions.
/// </param>
/// <param name="Category">Which of the two groups (transport listener or background service) the service belongs to.</param>
/// <param name="IsRunning">
///     Whether the service is running. For a DNS transport listener this is observed from the
///     listener; for a maintenance loop with no such signal, it means start was called more
///     recently than stop (see <see cref="IManagedServiceRegistration.IsRunning" />).
/// </param>
public record ManagedServiceSummary(
    string Name,
    ManagedServiceCategoryEnum Category,
    bool IsRunning
);

using Ninlil.Dns.Server.Core.Services;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This interface describes the set of <see cref="IManagedServiceRegistration" /> instances
///     the host runs: every DNS transport listener and background maintenance loop, each with the
///     stable name and category <c>ServicesController</c> exposes at <c>/api/services</c>.
///     <see cref="IManagedServiceRegistration" /> lives in <c>Server.Core</c> and knows nothing
///     about the concrete services; this registry lives in <c>Ninlil.Web</c>, the only project
///     that references all of them.
/// </summary>
public interface IManagedServiceRegistry
{
    /// <summary>
    ///     This property contains every registered service. The order is not guaranteed, so a
    ///     caller that needs a stable order, such as <c>GET /api/services</c>, sorts it.
    /// </summary>
    IReadOnlyList<IManagedServiceRegistration> Services { get; }

    /// <summary>
    ///     This method looks up one registered service by its
    ///     <see cref="IManagedServiceRegistration.Name" />, case-sensitively: names are fixed
    ///     kebab-case constants, so there is nothing to normalize.
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <returns>The matching registration, or <see langword="null" /> if none matches.</returns>
    IManagedServiceRegistration Find(string name);
}

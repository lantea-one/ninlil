using Ninlil.Dns.Server.Core.Services;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This interface describes the full set of <see cref="IManagedServiceRegistration" />
///     instances this host actually runs -- every DNS transport listener and every background
///     maintenance loop, each wrapped with the stable name/category a future
///     <c>ServicesController</c> exposes them under. Kept separate from
///     <see cref="IManagedServiceRegistration" /> itself (which lives in <c>Server.Core</c> and
///     knows nothing about which concrete services exist) since only this composition root
///     (<c>Ninlil.Web</c>) references every project a real service lives in.
/// </summary>
public interface IManagedServiceRegistry
{
    /// <summary>
    ///     This property contains every registered service, in no particular guaranteed order --
    ///     a caller wanting a stable presentation order (a <c>GET /api/services</c> listing, for
    ///     example) should sort this itself.
    /// </summary>
    IReadOnlyList<IManagedServiceRegistration> Services { get; }

    /// <summary>
    ///     This method looks up a single registered service by its own stable
    ///     <see cref="IManagedServiceRegistration.Name" />, case-sensitively (names are this
    ///     project's own kebab-case constants, never operator-supplied, so there is no
    ///     normalization question to resolve here the way there is for, say, a DNS domain name).
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <returns>The matching registration, or <see langword="null" /> if none matches.</returns>
    IManagedServiceRegistration Find(string name);
}

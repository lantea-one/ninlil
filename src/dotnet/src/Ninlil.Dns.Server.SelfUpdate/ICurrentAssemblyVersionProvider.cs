// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This interface reports the version of the running process, which
///     <see cref="SelfUpdateCheckService" /> compares with a fetched
///     <see cref="SelfUpdateReleaseFeedDocument.Version" />. It exists so tests can supply a
///     fixed version instead of the test host's; the application uses
///     <see cref="DefaultCurrentAssemblyVersionProvider" />.
/// </summary>
public interface ICurrentAssemblyVersionProvider
{
    /// <summary>
    ///     This method returns the version of the running process.
    /// </summary>
    Version GetCurrentVersion();
}

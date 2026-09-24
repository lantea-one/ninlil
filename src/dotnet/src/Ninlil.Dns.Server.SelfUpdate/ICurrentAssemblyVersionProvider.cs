// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This interface reports this running process's own current version, so
///     <see cref="SelfUpdateCheckService" /> has something real to compare a fetched
///     <see cref="SelfUpdateReleaseFeedDocument.Version" /> against. Extracted behind an
///     interface (rather than a plain static call to <see cref="System.Reflection.Assembly.GetEntryAssembly" />
///     inline) purely so a test can supply a deterministic, injected version instead of whatever
///     the actual test host assembly happens to report -- the real, shipped
///     <see cref="DefaultCurrentAssemblyVersionProvider" /> is what every non-test caller actually
///     gets.
/// </summary>
public interface ICurrentAssemblyVersionProvider
{
    /// <summary>
    ///     This method returns this running process's own current version.
    /// </summary>
    Version GetCurrentVersion();
}

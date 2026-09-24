using System.Reflection;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class implements <see cref="ICurrentAssemblyVersionProvider" /> by reading the
///     hosting process's own entry assembly version, in every real deployment of this project
///     that is <c>Ninlil.Web</c> itself, the single combined host every transport, the REST
///     API, and the Angular SPA all run inside. Falls back to this very assembly's own version if
///     no entry assembly is available at all (a scenario that should never occur in a real hosted
///     process, but keeps this provider total rather than throwing).
///     <para>
///         The version this method reports is the real, explicit <c>&lt;Version&gt;</c> this
///         project's own <c>Directory.Build.props</c> sets, shared by every assembly in the
///         solution, not the .NET SDK's own implicit default (<c>1.0.0.0</c>) a project with no
///         explicit version would otherwise carry. See that property's own doc comment in
///         <c>Directory.Build.props</c> for why it deliberately starts in the <c>0.x.y</c> range
///         rather than claiming a <c>1.0.0</c> stability this project has not actually reached
///         yet, and for how it is expected to evolve. This project still has no real
///         release-artifact or installer story (see <see cref="SelfUpdateOptions" />'s own doc
///         comment), so a comparison against a fetched release feed is only ever as meaningful as
///         whatever that feed itself actually tracks, but the version reported here is now this
///         running process's own real, specific build number rather than a placeholder shared by
///         every build regardless of what it actually contains.
///     </para>
/// </summary>
public sealed class DefaultCurrentAssemblyVersionProvider : ICurrentAssemblyVersionProvider
{
    /// <inheritdoc />
    public Version GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0, 0, 0);
}

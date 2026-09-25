using System.Reflection;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class implements <see cref="ICurrentAssemblyVersionProvider" /> by reading the
///     version of the entry assembly, which in a deployment is <c>Ninlil.Web</c>, the single
///     host that runs every DNS transport, the REST API and the web UI. If there is no entry
///     assembly it falls back to this assembly's version, and then to <c>0.0.0.0</c>, so it
///     never throws.
///     <para>
///         The version comes from <c>Directory.Build.props</c>. The build pipeline
///         (<c>.forgejo/scripts/compute-version.sh</c>) stamps the date-based
///         <c>yyyy.m.d.increment</c> assembly version on each build; a local build is
///         <c>0.0.0-local</c>, whose assembly version is <c>0.0.0.0</c>, so any configured feed
///         reports an update for it.
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

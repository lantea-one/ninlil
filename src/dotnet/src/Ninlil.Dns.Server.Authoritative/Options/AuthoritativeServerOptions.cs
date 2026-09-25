// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative</c> section of the application's
///     configuration and holds the authoritative engine's settings that are not zone data: the
///     CHAOS version and hostname answers. The branding settings endpoint edits it, and
///     <see cref="Resolution.AuthoritativeQueryResolver" /> reads it on every CHAOS query.
/// </summary>
public class AuthoritativeServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative";

    /// <summary>
    ///     This property controls whether this server answers the CHAOS-class TXT queries
    ///     <c>version.bind</c>, <c>version.server</c>, <c>hostname.bind</c> and <c>id.server</c>,
    ///     which identify a server's software and host. Off by default, since the answers mostly
    ///     help an attacker looking for a vulnerable version.
    /// </summary>
    public bool EnableChaosVersionQueries { get; set; }

    /// <summary>
    ///     This property contains the value returned for a <c>version.bind</c>/
    ///     <c>version.server</c> query, when <see cref="EnableChaosVersionQueries" /> is enabled.
    /// </summary>
    public string VersionString { get; set; } = "Ninlil";

    /// <summary>
    ///     This property contains the value returned for a <c>hostname.bind</c>/<c>id.server</c>
    ///     query, when <see cref="EnableChaosVersionQueries" /> is enabled. Defaults to the
    ///     machine's host name.
    /// </summary>
    public string HostnameString { get; set; } = Environment.MachineName;
}

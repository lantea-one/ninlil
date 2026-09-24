// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative</c> section of the application's
///     configuration and controls the authoritative engine's own behavior, separate from the
///     zone data it serves.
/// </summary>
public class AuthoritativeServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative";

    /// <summary>
    ///     This property controls whether this server answers the traditional CHAOS-class
    ///     <c>version.bind</c>/<c>version.server</c>/<c>hostname.bind</c>/<c>id.server</c> TXT
    ///     queries used to fingerprint a name server's software and host. Off by default: a
    ///     server has no obligation to hand its version and hostname to anyone who asks, and
    ///     doing so by default only helps an attacker looking for known vulnerabilities in a
    ///     specific version.
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
    ///     machine's own host name.
    /// </summary>
    public string HostnameString { get; set; } = Environment.MachineName;
}

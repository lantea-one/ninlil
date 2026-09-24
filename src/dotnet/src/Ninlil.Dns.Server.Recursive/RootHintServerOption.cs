// Define our namespace.
namespace Ninlil.Dns.Server.Recursive;

/// <summary>
///     This class binds one entry of <see cref="RecursiveServerOptions.RootHintServersOverride" />
///     -- an operator-configured replacement for the hardcoded <see cref="RootHints" /> list, for
///     a deployment that recurses against something other than the real internet root (an
///     internal-only resolver forwarding to a private root, or a test double).
/// </summary>
public sealed class RootHintServerOption
{
    /// <summary>
    ///     This property contains the name server's own name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the name server's own addresses, as plain strings (parsed via
    ///     <see cref="System.Net.IPAddress.Parse(string)" />).
    /// </summary>
    public List<string> Addresses { get; set; } = [];
}

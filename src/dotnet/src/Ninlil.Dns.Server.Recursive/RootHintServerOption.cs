// Define our namespace.
namespace Ninlil.Dns.Server.Recursive;

/// <summary>
///     This class binds one entry of <see cref="RecursiveServerOptions.RootHintServersOverride" />,
///     which replaces the built-in <see cref="RootHints" /> list for a deployment that recurses
///     against a private root or a test double instead of the internet root.
/// </summary>
public sealed class RootHintServerOption
{
    /// <summary>
    ///     This property contains the name server's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the name server's addresses, as strings (parsed with
    ///     <see cref="System.Net.IPAddress.Parse(string)" />).
    /// </summary>
    public List<string> Addresses { get; set; } = [];
}

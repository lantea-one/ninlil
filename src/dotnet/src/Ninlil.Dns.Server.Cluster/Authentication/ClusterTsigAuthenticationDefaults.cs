// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class contains the well-known names <see cref="ClusterTsigAuthenticationHandler" />
///     and every cluster-channel caller (<c>ClusterPeerHttpClientFactory</c>) both need to agree
///     on -- the authentication scheme's own registered name, and the header a signed request
///     carries its signature in.
/// </summary>
public static class ClusterTsigAuthenticationDefaults
{
    /// <summary>
    ///     This constant contains the name this scheme is registered under with ASP.NET Core's
    ///     authentication middleware. Referenced directly by every cluster-channel controller
    ///     action's own <c>[Authorize(AuthenticationSchemes = ...)]</c> attribute -- this scheme
    ///     is never part of <c>ServiceCollectionAuthenticationExtension</c>'s own JWT/API-token
    ///     forwarding selection, so an ordinary human-operator request can never accidentally
    ///     authenticate against it, and a peer request can never accidentally authenticate as a
    ///     human operator.
    /// </summary>
    public const string AuthenticationScheme = "ClusterTsig";

    /// <summary>
    ///     This constant contains the name of the HTTP header a signed cluster-channel request
    ///     carries its own <c>&lt;key name&gt;:&lt;unix timestamp&gt;:&lt;base64 MAC&gt;</c>
    ///     signature in -- see <see cref="ClusterTsigRequestSigner" /> for exactly how that value
    ///     is computed and parsed.
    /// </summary>
    public const string SignatureHeaderName = "X-Ninlil-Cluster-Signature";
}

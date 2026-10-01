// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class contains the names <see cref="ClusterTsigAuthenticationHandler" /> and the
///     sending side (<see cref="Http.ClusterPeerHttpClientFactory" />) must agree on: the
///     authentication scheme name and the signature header.
/// </summary>
public static class ClusterTsigAuthenticationDefaults
{
    /// <summary>
    ///     This constant contains the name this scheme is registered under with ASP.NET Core's
    ///     authentication middleware. Cluster-channel actions name it in their
    ///     <c>[Authorize(AuthenticationSchemes = ...)]</c> attribute. It is not part of the
    ///     JWT/API-token selection of <c>ServiceCollectionAuthenticationExtension</c>, so an
    ///     operator's token cannot reach a peer endpoint and a peer signature cannot reach an
    ///     operator endpoint.
    /// </summary>
    public const string AuthenticationScheme = "ClusterTsig";

    /// <summary>
    ///     This constant contains the HTTP header that carries a request's
    ///     <c>&lt;key name&gt;:&lt;unix timestamp&gt;:&lt;base64 MAC&gt;</c> signature (see
    ///     <see cref="ClusterTsigRequestSigner" />).
    /// </summary>
    public const string SignatureHeaderName = "X-Ninlil-Cluster-Signature";
}

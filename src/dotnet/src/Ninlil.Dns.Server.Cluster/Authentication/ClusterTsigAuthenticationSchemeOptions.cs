using Microsoft.AspNetCore.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class configures <see cref="ClusterTsigAuthenticationHandler" />. It carries no
///     scheme-specific settings of its own today -- the header name this handler reads is a
///     fixed constant (<see cref="ClusterTsigAuthenticationDefaults.SignatureHeaderName" />)
///     rather than per-instance configuration, since this scheme (unlike
///     <c>ApiTokenAuthenticationSchemeOptions</c>'s own configurable header name/prefix) is never
///     meant to be repointed at a different header by configuration -- but the type still exists
///     as its own class, matching <see cref="AuthenticationHandler{TOptions}" />'s own generic
///     contract, and gives a natural home for a future scheme-specific setting (for example, a
///     narrower clock-skew tolerance) without a breaking signature change.
/// </summary>
public class ClusterTsigAuthenticationSchemeOptions : AuthenticationSchemeOptions;

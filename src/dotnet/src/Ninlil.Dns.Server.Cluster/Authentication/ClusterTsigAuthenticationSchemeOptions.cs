using Microsoft.AspNetCore.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class configures <see cref="ClusterTsigAuthenticationHandler" />. It has no settings
///     of its own: the header name is the fixed
///     <see cref="ClusterTsigAuthenticationDefaults.SignatureHeaderName" />, unlike the
///     configurable header of <c>ApiTokenAuthenticationSchemeOptions</c>. It exists because
///     <see cref="AuthenticationHandler{TOptions}" /> needs an options type.
/// </summary>
public class ClusterTsigAuthenticationSchemeOptions : AuthenticationSchemeOptions;

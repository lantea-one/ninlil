using Microsoft.AspNetCore.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authentication;

/// <summary>
///     This class configures how <see cref="ApiTokenAuthenticationHandler" /> reads an API token
///     out of an incoming request.
/// </summary>
public class ApiTokenAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>
    ///     This property contains the name of the header the token is read from.
    /// </summary>
    public string HeaderName { get; set; } = "Authorization";

    /// <summary>
    ///     This property contains the scheme prefix a header value must start with -- for
    ///     example, <c>Authorization: ApiToken &lt;token&gt;</c> -- distinguishing an API token
    ///     from, say, a <c>Bearer</c>-prefixed JSON Web Token sharing the same header.
    /// </summary>
    public string SchemePrefix { get; set; } = "ApiToken";
}

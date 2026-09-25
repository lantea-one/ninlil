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
    ///     This property contains the prefix a header value must start with, as in
    ///     <c>Authorization: ApiToken &lt;token&gt;</c>. It tells an API token apart from a
    ///     <c>Bearer</c> JWT in the same header.
    /// </summary>
    public string SchemePrefix { get; set; } = "ApiToken";
}

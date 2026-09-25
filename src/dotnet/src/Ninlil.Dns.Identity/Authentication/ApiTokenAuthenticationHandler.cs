using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Claims;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authentication;

/// <summary>
///     This class authenticates a request that presents an API token instead of a JWT, for
///     example <c>Authorization: ApiToken 7f2e...</c>. It validates (and, for a one-time token,
///     consumes) the token through <see cref="IApiTokenService" />, then builds the same claims a
///     JWT request carries through <see cref="IUserClaimsBuilder" />, so authorization does not
///     depend on which credential was used.
/// </summary>
/// <param name="options">The scheme's configured header name and prefix.</param>
/// <param name="logger">The logger factory used to create this handler's diagnostic logger.</param>
/// <param name="encoder">The URL encoder ASP.NET Core's base handler class requires.</param>
/// <param name="apiTokenService">The service used to validate the presented token.</param>
/// <param name="userRepository">The repository used to look up the token's owning user.</param>
/// <param name="claimsBuilder">The service used to build the authenticated user's claims.</param>
public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<ApiTokenAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiTokenService apiTokenService,
    IUserRepository userRepository,
    IUserClaimsBuilder claimsBuilder
) : AuthenticationHandler<ApiTokenAuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No header: leave the request to the other authentication schemes.
        if (!Request.Headers.TryGetValue(Options.HeaderName, out StringValues headerValues))
            return AuthenticateResult.NoResult();

        string headerValue = headerValues.ToString();
        string prefix = $"{Options.SchemePrefix} ";

        // A different prefix (a "Bearer" JWT, for example) belongs to another scheme.
        if (!headerValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string rawToken = headerValue[prefix.Length..].Trim();

        if (string.IsNullOrEmpty(rawToken))
            return AuthenticateResult.Fail("The API token header was present but empty.");

        ApiTokenEntity token = await apiTokenService.ValidateAsync(rawToken);

        if (token is null)
            return AuthenticateResult.Fail("The API token is invalid, expired, or revoked.");

        UserEntity user = await userRepository.GetByIdAsync(token.UserId);

        if (user is null or { IsDisabled: true })
            return AuthenticateResult.Fail(
                "The API token's owning account no longer exists or is disabled."
            );

        ClaimsIdentity identity = new(await claimsBuilder.BuildClaimsAsync(user), Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}

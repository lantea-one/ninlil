using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
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
///     example <c>Authorization: ApiToken 7f2e...</c>. It validates the token and, once the owning
///     account is accepted, consumes a one-time token through <see cref="IApiTokenService" />, then builds the same claims a
///     JWT request carries through <see cref="IUserClaimsBuilder" />, so authorization does not
///     depend on which credential was used.
///     <para>
///         The handler does all of its database work in a scope of its own, not in the request's
///         scope. Authentication runs before the storage read intent middleware decides whether
///         the endpoint may read a replica, and the first repository a scope resolves fixes that
///         scope's intent. Validation, the one-time consume and the claims must read the primary
///         (a revoked token or a changed permission has to apply at once), so they get a primary
///         scope here and leave the request scope free for the endpoint's own choice.
///     </para>
/// </summary>
/// <param name="options">The scheme's configured header name and prefix.</param>
/// <param name="logger">The logger factory used to create this handler's diagnostic logger.</param>
/// <param name="encoder">The URL encoder ASP.NET Core's base handler class requires.</param>
/// <param name="scopeFactory">
///     Creates the scope in which the token is validated, its owner is looked up and the claims
///     are built (see the class summary).
/// </param>
public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<ApiTokenAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory
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

        // A scope of its own, see the class summary. Its intent stays the default, the primary.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApiTokenService apiTokenService =
            scope.ServiceProvider.GetRequiredService<IApiTokenService>();
        IUserRepository userRepository =
            scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IUserClaimsBuilder claimsBuilder =
            scope.ServiceProvider.GetRequiredService<IUserClaimsBuilder>();

        ApiTokenEntity token = await apiTokenService.ValidateAsync(rawToken);

        if (token is null)
            return AuthenticateResult.Fail("The API token is invalid, expired, or revoked.");

        UserEntity user = await userRepository.GetByIdAsync(token.UserId);

        if (
            user is null
            || user.IsDisabled
            || (user.LockoutEnabled && user.LockoutEndUtc > DateTimeOffset.UtcNow)
        )
            return AuthenticateResult.Fail(
                "The API token's owning account no longer exists, is disabled or is locked out."
            );

        // Consume only now that the account is accepted, so a rejected request leaves a one-time
        // token usable. A lost race with another request presenting the same token fails here.
        if (!await apiTokenService.ConsumeAsync(token))
            return AuthenticateResult.Fail("The API token is invalid, expired, or revoked.");

        ClaimsIdentity identity = new(await claimsBuilder.BuildClaimsAsync(user), Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}

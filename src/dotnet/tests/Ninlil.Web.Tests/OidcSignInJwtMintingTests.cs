using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Identity.Token;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the part of OIDC sign-in that needs no external identity provider. It
///     builds a <see cref="ClaimsPrincipal" /> shaped like the one a completed handshake hands
///     <c>OpenIdConnectOptions.Events.OnTokenValidated</c> (see
///     <see cref="ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication" />), passes
///     it to the shared host's <see cref="IOidcUserProvisioningService" /> and
///     <see cref="IJwtTokenService" />, and checks that the minted access token is accepted by the
///     host's JWT bearer scheme on a protected endpoint.
///     <para>
///         The redirect chain itself (<see cref="Controllers.SsoController.StartChallenge" />, the
///         identity provider's login form and the callback) needs a live identity provider and is
///         not tested. <see cref="SsoControllerTests" /> covers the not-configured case.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class OidcSignInJwtMintingTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task AHandCraftedOidcPrincipalProvisionsAnAccountAndMintsAWorkingAccessToken()
    {
        // The principal OnTokenValidated would pass on, with claims under their OIDC short
        // names ("sub", "email", ...), since MapInboundClaims is false.
        ClaimsPrincipal principal = new(
            new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, "smoke-test-subject"),
                    new Claim("preferred_username", "smoke-test-user"),
                    new Claim("name", "Smoke Test User"),
                    new Claim(JwtRegisteredClaimNames.Email, "smoke-test@example.com"),
                ],
                "oidc"
            )
        );

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IOidcUserProvisioningService provisioningService =
            scope.ServiceProvider.GetRequiredService<IOidcUserProvisioningService>();
        IJwtTokenService jwtTokenService =
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        UserEntity user = await provisioningService.FindOrProvisionUserAsync(principal);
        Assert.Equal("smoke-test-user", user.UserName);

        AccessTokenResult accessToken = await jwtTokenService.CreateAccessTokenAsync(user);
        Assert.False(string.IsNullOrWhiteSpace(accessToken.AccessToken));

        // The host's JWT bearer scheme must accept the token on a permission-gated endpoint. A
        // newly provisioned OIDC account has no permissions (see OidcUserProvisioningService), so
        // the answer is 403 Forbidden rather than 401 Unauthorized: authentication succeeded and
        // only authorization failed.
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.AccessToken
        );

        HttpResponseMessage response = await client.GetAsync("/api/dns64-settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

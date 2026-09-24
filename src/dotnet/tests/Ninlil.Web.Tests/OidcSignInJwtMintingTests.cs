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
///     This class proves the one part of the full OIDC sign-in redirect chain that does not need
///     a live external identity provider to verify for real: given a
///     <see cref="ClaimsPrincipal" /> shaped exactly like the one a completed handshake would hand
///     <c>OpenIdConnectOptions.Events.OnTokenValidated</c> (see
///     <see cref="ServiceCollectionAuthenticationExtension.AddNinlilDnsAuthentication" />'s own
///     doc comment for that event's real body), <see cref="IOidcUserProvisioningService" /> and
///     <see cref="IJwtTokenService" /> -- resolved from this real, running host's own dependency
///     injection container, not new stand-ins -- genuinely mint a working Ninlil access token:
///     one this same host's own JWT bearer scheme accepts against a real protected endpoint.
///     <para>
///         A full live-IdP round trip (actually reaching <see cref="Controllers.SsoController.StartChallenge" />,
///         following a redirect to a real identity provider, and completing its own login form)
///         is out of scope here, matching every other ACME/live-CA/live-broker feature in this
///         project that is verified only up to the boundary of a real external service this
///         environment cannot stand one up for -- see <see cref="SsoControllerTests" />'s own doc
///         comment for the "not configured" side this class does not repeat.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class OidcSignInJwtMintingTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task AHandCraftedOidcPrincipalProvisionsAnAccountAndMintsAWorkingAccessToken()
    {
        // Build the exact ClaimsPrincipal shape a real completed OIDC handshake's own
        // OnTokenValidated event would hand IOidcUserProvisioningService -- claims under their
        // own real OIDC short names ("sub", "email", ...), matching MapInboundClaims = false.
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

        // The real proof: this host's own JWT bearer scheme (Program.cs's
        // AddNinlilDnsAuthentication) genuinely accepts the token this test just minted, against
        // a real permission-gated endpoint -- not merely "a JWT string with the right shape got
        // produced." A freshly-provisioned OIDC account holds no permissions at all (by design,
        // see OidcUserProvisioningService's own doc comment), so /api/dns64-settings correctly
        // still rejects it with 403 Forbidden, not 401 Unauthorized -- proving authentication
        // succeeded and only authorization (as expected) did not.
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.AccessToken
        );

        HttpResponseMessage response = await client.GetAsync("/api/dns64-settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

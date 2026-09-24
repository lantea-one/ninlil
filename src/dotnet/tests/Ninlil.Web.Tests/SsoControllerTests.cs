using System.Net;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="Controllers.SsoController" /> against the shared test host,
///     which deliberately never configures <c>Identity:Sso</c> (see
///     <see cref="NinlilDnsWebApplicationFactory" />'s own doc comment on why this factory's
///     environment-variable overrides are process-wide, shared across every test class in this
///     collection) -- so every assertion here proves the "not configured" side of both actions:
///     <see cref="Get_ReportsDisabledWhenNoIdentityProviderIsConfigured" />/
///     <see cref="Challenge_ReturnsNotFoundWhenNoIdentityProviderIsConfigured" />. The "actually
///     configured and working" side of this feature needs a real (or realistically faked) OIDC
///     issuer to redirect to, which is out of scope for this shared, general-purpose host --
///     <c>Ninlil.Dns.Identity.Tests.OidcUserProvisioningServiceTests</c> and
///     <see cref="OidcSignInJwtMintingTests" /> cover the parts of that flow this project can
///     verify without one.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SsoControllerTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task Get_ReportsDisabledWhenNoIdentityProviderIsConfigured()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/sso");
        response.EnsureSuccessStatusCode();

        SsoStatusResult status = await response.Content.ReadFromJsonAsync<SsoStatusResult>();

        Assert.False(status.Enabled);
    }

    [Fact]
    public async Task Get_RequiresNoAuthenticationAtAll()
    {
        // A brand-new, never-authenticated client -- this action must work for the exact
        // audience it exists for: an anonymous caller on the login page, before any sign-in.
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/sso");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Challenge_ReturnsNotFoundWhenNoIdentityProviderIsConfigured()
    {
        HttpClient client = factory.CreateClient();

        // A real browser navigation would follow this without HttpClient's own default
        // auto-redirect behavior mattering (there is nothing to redirect to here at all) --
        // 404, matching Get's own enabled:false, rather than an unhandled server error.
        HttpResponseMessage response = await client.GetAsync("/api/auth/sso/challenge");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

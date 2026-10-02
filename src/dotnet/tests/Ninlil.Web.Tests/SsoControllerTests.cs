using System.Net;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="Controllers.SsoController" /> over HTTP on the shared
///     in-process host, which does not configure <c>Identity:Sso</c>, so only the not-configured
///     side is tested: <see cref="Get_ReportsDisabledWhenNoIdentityProviderIsConfigured" />, the
///     anonymous access to <c>Get</c>, and
///     <see cref="Challenge_ReturnsNotFoundWhenNoIdentityProviderIsConfigured" />. The configured
///     flow needs an OIDC issuer to redirect to;
///     <c>Ninlil.Dns.Identity.Tests.OidcUserProvisioningServiceTests</c> and
///     <see cref="OidcSignInJwtMintingTests" /> cover the parts that work without one.
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
        // A client with no token: the login page calls this action before anyone signs in.
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/sso");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Challenge_ReturnsNotFoundWhenNoIdentityProviderIsConfigured()
    {
        HttpClient client = factory.CreateClient();

        // With nothing configured there is no redirect, so the answer is 404, matching Get's
        // enabled:false, rather than a server error.
        HttpResponseMessage response = await client.GetAsync("/api/auth/sso/challenge");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

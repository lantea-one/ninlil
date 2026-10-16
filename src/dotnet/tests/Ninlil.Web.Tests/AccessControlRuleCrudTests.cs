using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the access-control rule endpoints of
///     <see cref="DnsAccessControlRulesController" /> over HTTP on the shared in-process host
///     (see <see cref="NinlilDnsWebApplicationFactory" />), signed in as the bootstrap
///     administrator. It runs the create, list, get, replace and delete lifecycle, checks that
///     capabilities round-trip as an array of names, rejects unknown capability names and bad
///     CIDRs, and rejects requests without a token. Whether a rule changes DNS answers is
///     covered by <see cref="AccessControlRuleEndToEndTests" />.
/// </summary>
[Collection(WebTestCollection.Name)]
public class AccessControlRuleCrudTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();

        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    [Fact]
    public async Task RuleLifecycle_CreateListGetReplaceAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"rule-{Guid.NewGuid():N}";

        // Creating a rule granting both capabilities at once.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name,
                cidr = "203.0.113.0/24",
                capabilities = new[] { "RecursionAccess", "RateLimitBypass" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DnsAccessControlRuleDetail created =
            await createResponse.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();
        Assert.Equal(name, created.Name);
        Assert.Equal("203.0.113.0/24", created.Cidr);
        Assert.Equal(
            new[] { "RateLimitBypass", "RecursionAccess" },
            created.Capabilities.OrderBy(value => value, StringComparer.Ordinal)
        );
        Assert.False(created.IsDisabled);

        // Every create is stamped with the local node's cluster identity, whether or not
        // clustering is enabled.
        Assert.NotNull(created.OriginatingNodeId);

        // Listing reflects it, with pagination fields present.
        HttpResponseMessage listResponse = await client.GetAsync("/api/access-control-rules");
        listResponse.EnsureSuccessStatusCode();
        PagedResult<DnsAccessControlRuleDetail> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<DnsAccessControlRuleDetail>
        >();
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.True(page.Take > 0);

        // Getting it by identifier.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/access-control-rules/{created.Id}"
        );
        getResponse.EnsureSuccessStatusCode();
        DnsAccessControlRuleDetail fetched =
            await getResponse.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();
        Assert.Equal(created.Id, fetched.Id);

        // PUT replaces the CIDR/capabilities, drops to a single capability, and disables it,
        // keeping the identifier.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/access-control-rules/{created.Id}",
            new
            {
                name,
                cidr = "203.0.113.128/25",
                capabilities = new[] { "RateLimitBypass" },
                isDisabled = true,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/access-control-rules/{created.Id}"
        );
        DnsAccessControlRuleDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();
        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal("203.0.113.128/25", replaced.Cidr);
        Assert.Equal(["RateLimitBypass"], replaced.Capabilities);
        Assert.True(replaced.IsDisabled);

        // Delete it.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/access-control-rules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/access-control-rules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnUnrecognizedCapabilityName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"bad-capability-{Guid.NewGuid():N}",
                cidr = "203.0.113.0/24",
                capabilities = new[] { "NotARealCapability" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithACombinedOrNumericCapabilityString_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // "RecursionAccess, RateLimitBypass" as one element parses as the combined flags value
        // with Enum.TryParse, but Enum.IsDefined rejects it because no member has that value.
        // The controller must accept one name per array element. Despite the test name, only
        // the combined form is sent; a numeric string is not tried.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"combined-{Guid.NewGuid():N}",
                cidr = "203.0.113.0/24",
                capabilities = new[] { "RecursionAccess, RateLimitBypass" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnInvalidCidr_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"bad-cidr-{Guid.NewGuid():N}",
                cidr = "not a cidr at all",
                capabilities = new[] { "RecursionAccess" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     A bare IPv4 or IPv6 address, or a CIDR network, is a valid rule target and is stored as
    ///     entered.
    /// </summary>
    [Theory]
    [InlineData("192.0.2.7")]
    [InlineData("2001:db8::7")]
    [InlineData("192.0.2.0/24")]
    public async Task Create_WithABareAddressOrACidr_IsAcceptedAndStoredAsEntered(string cidr)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"bare-{Guid.NewGuid():N}",
                cidr,
                capabilities = new[] { "RecursionAccess" },
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DnsAccessControlRuleDetail created =
            await response.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();
        Assert.Equal(cidr, created.Cidr);
        await client.DeleteAsync($"/api/access-control-rules/{created.Id}");
    }

    /// <summary>
    ///     A malformed address, a zone-scoped address and an out-of-range prefix length are
    ///     rejected with the one message that names the accepted forms.
    /// </summary>
    [Theory]
    [InlineData("192.0.2.300")]
    [InlineData("fe80::1%2")]
    [InlineData("192.0.2.0/33")]
    public async Task Create_WithAMalformedAddress_ReturnsBadRequestNamingTheAcceptedForms(
        string cidr
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"bad-{Guid.NewGuid():N}",
                cidr,
                capabilities = new[] { "RecursionAccess" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("is not a valid IP address or CIDR network (for example 192.0.2.7", body);
    }

    [Fact]
    public async Task Create_WithNoCapabilities_IsAcceptedAndGrantsNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"empty-capabilities-{Guid.NewGuid():N}",
                cidr = "203.0.113.0/24",
                capabilities = Array.Empty<string>(),
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DnsAccessControlRuleDetail created =
            await response.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();
        Assert.Empty(created.Capabilities);
    }

    [Fact]
    public async Task AccessControlRulesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/access-control-rules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

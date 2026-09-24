using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="FilterPatternPreviewController" /> end to end, against a
///     real, in-process host -- confirming the preview reuses the exact same
///     <c>DnsFilterEngine.CompileOrNull</c>/<c>DnsFilterEngine.BuildWildcardRegexPattern</c>
///     compilation the real filter engine and <see cref="FilterListEntriesController" />'s own
///     write-time validation already use, for every <see cref="DnsFilterEntryKindEnum" /> value.
/// </summary>
[Collection(WebTestCollection.Name)]
public class FilterPatternPreviewControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task Preview_WithAWildcardPattern_MatchesOnlyTheExpectedDefaultSampleDomains()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-pattern-preview",
            new FilterPatternPreviewRequest(DnsFilterEntryKindEnum.Wildcard, "*.example.com")
        );
        response.EnsureSuccessStatusCode();

        FilterPatternPreviewResult result =
            await response.Content.ReadFromJsonAsync<FilterPatternPreviewResult>();
        Assert.True(result.IsValid);
        Assert.Null(result.Error);

        // "*.example.com" matches every subdomain but not the bare apex itself (the wildcard
        // glob requires the literal "." before "example.com").
        Assert.False(Match(result, "example.com"));
        Assert.True(Match(result, "www.example.com"));
        Assert.True(Match(result, "shop.example.com"));
        Assert.False(Match(result, "youtube.com"));
    }

    [Fact]
    public async Task Preview_WithARegexPattern_MatchesExactlyWhatDotNetRegexWould()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-pattern-preview",
            new FilterPatternPreviewRequest(DnsFilterEntryKindEnum.Regex, "^ads?\\..+\\.com$")
        );
        response.EnsureSuccessStatusCode();

        FilterPatternPreviewResult result =
            await response.Content.ReadFromJsonAsync<FilterPatternPreviewResult>();
        Assert.True(result.IsValid);
        Assert.True(Match(result, "ads.example.com"));
        Assert.False(Match(result, "www.example.com"));
    }

    [Fact]
    public async Task Preview_WithADomainKindPattern_MatchesTheDomainAndItsSubdomainsOnly()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-pattern-preview",
            new FilterPatternPreviewRequest(DnsFilterEntryKindEnum.Domain, "example.com")
        );
        response.EnsureSuccessStatusCode();

        FilterPatternPreviewResult result =
            await response.Content.ReadFromJsonAsync<FilterPatternPreviewResult>();
        Assert.True(result.IsValid);
        Assert.True(Match(result, "example.com"));
        Assert.True(Match(result, "www.example.com"));
        // "unrelated-domain.org" (not "notexample.com", which isn't in the controller's own
        // DefaultSampleDomains at all and would make Match() throw) -- a real, unrelated domain
        // from the default sample set, proving this isn't a naive substring match.
        Assert.False(Match(result, "unrelated-domain.org"));
    }

    [Fact]
    public async Task Preview_WithAMalformedRegex_ReturnsInvalidWithNoMatches()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-pattern-preview",
            new FilterPatternPreviewRequest(DnsFilterEntryKindEnum.Regex, "(unterminated")
        );
        response.EnsureSuccessStatusCode();

        FilterPatternPreviewResult result =
            await response.Content.ReadFromJsonAsync<FilterPatternPreviewResult>();
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Preview_WithCallerSuppliedSampleDomains_TestsOnlyThoseDomains()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-pattern-preview",
            new FilterPatternPreviewRequest(
                DnsFilterEntryKindEnum.Domain,
                "custom.test",
                ["custom.test", "other.test"]
            )
        );
        response.EnsureSuccessStatusCode();

        FilterPatternPreviewResult result =
            await response.Content.ReadFromJsonAsync<FilterPatternPreviewResult>();
        Assert.Equal(2, result.Matches.Count);
        Assert.True(Match(result, "custom.test"));
        Assert.False(Match(result, "other.test"));
    }

    [Fact]
    public async Task FilterPatternPreviewEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-pattern-preview",
            new FilterPatternPreviewRequest(DnsFilterEntryKindEnum.Domain, "example.com")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static bool Match(FilterPatternPreviewResult result, string domain) =>
        result.Matches.Single(match => match.Domain == domain).IsMatch;
}

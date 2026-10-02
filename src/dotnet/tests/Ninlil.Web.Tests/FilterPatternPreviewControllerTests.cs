using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="FilterPatternPreviewController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. It checks the match results for
///     wildcard, regex and domain patterns against the default sample domains and against
///     caller-supplied ones, and that a malformed regex is reported as invalid. The preview
///     compiles patterns with <c>DnsFilterEngine.CompileOrNull</c> and
///     <c>DnsFilterEngine.BuildWildcardRegexPattern</c>, as the filter engine and the write-time
///     validation of <see cref="FilterListEntriesController" /> do.
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

        // "*.example.com" matches every subdomain but not the apex, because the glob requires
        // the "." before "example.com".
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
        // "unrelated-domain.org" is an unrelated default sample domain. A look-alike such as
        // "notexample.com" is not in DefaultSampleDomains (Match() would throw), so a substring
        // match is not ruled out here.
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

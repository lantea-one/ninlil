using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="QueryLogController" /> and
///     <see cref="QueryLoggingSettingsController" /> over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: search filters, clearing the log, the default
///     logging settings and the reload notes a patch returns. A query-string enum filter
///     (<c>responseCode</c>, <c>queriedType</c>) must accept both the C# member name and the wire
///     string a response body shows, because for several
///     <see cref="Ninlil.Dns.Common.Enum.DnsResponseCodeEnum" /> members the two differ by more
///     than case, and a caller must be able to filter by a value read from an earlier result.
/// </summary>
[Collection(WebTestCollection.Name)]
public class QueryLogCrudTests(NinlilDnsWebApplicationFactory factory)
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

    [Theory]
    // "FORMERR", "SERVFAIL" and "NOTIMP" are the [EnumMember] wire strings of DnsResponseCodeEnum,
    // the text a response body shows, and differ from the member names by more than case.
    [InlineData("FORMERR")]
    [InlineData("SERVFAIL")]
    [InlineData("NOTIMP")]
    // The same three values spelled as their C# member names.
    [InlineData("FormatError")]
    [InlineData("ServerFailure")]
    [InlineData("NotImplemented")]
    public async Task Search_WithResponseCodeFilterInEitherSpelling_Succeeds(string responseCode)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/query-log?responseCode={responseCode}"
        );

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Search_WithAResponseCodeMatchingNeitherSpelling_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/query-log?responseCode=NOTAREALCODE"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithAQueriedTypeFilter_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/query-log?queriedType=A");

        response.EnsureSuccessStatusCode();

        PagedResult<DnsQueryLogEntryDetail> page = await response.Content.ReadFromJsonAsync<
            PagedResult<DnsQueryLogEntryDetail>
        >();
        Assert.NotNull(page);
    }

    [Fact]
    public async Task Delete_ClearsTheLog()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage deleteResponse = await client.DeleteAsync("/api/query-log");
        deleteResponse.EnsureSuccessStatusCode();

        QueryLogClearResult result =
            await deleteResponse.Content.ReadFromJsonAsync<QueryLogClearResult>();
        Assert.True(result.DeletedCount >= 0);

        HttpResponseMessage listResponse = await client.GetAsync("/api/query-log");
        PagedResult<DnsQueryLogEntryDetail> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<DnsQueryLogEntryDetail>
        >();
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task QueryLoggingSettings_Get_ReturnsTheOnByDefaultConfiguration()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/query-logging-settings");
        response.EnsureSuccessStatusCode();

        QueryLoggingSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<QueryLoggingSettingsDetail>();
        Assert.True(detail.Enable);
        Assert.Equal(QueryLogPrivacyLevelEnum.Full, detail.PrivacyLevel);
    }

    [Fact]
    public async Task QueryLogEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/query-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test confirms that a write touching only <c>privacyLevel</c> returns a "no restart
    ///     required" note. A later <c>GET</c> would still show the old value (see
    ///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />);
    ///     <see cref="QueryLoggingOptionsHotReloadTests" /> covers the reload.
    /// </summary>
    [Fact]
    public async Task QueryLoggingSettings_PatchPrivacyLevelOnly_ReportsNoRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage patchResponse = await client.PatchAsync(
            "/api/query-logging-settings",
            JsonContent.Create(new { privacyLevel = "ClientAnonymized" })
        );
        patchResponse.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await patchResponse.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("no restart", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     This test confirms that a write touching <c>privacyLevel</c> (hot-reloading) and
    ///     <c>retentionPeriod</c> (needs a restart) returns the mixed note, which names
    ///     <c>privacyLevel</c> and mentions a restart.
    /// </summary>
    [Fact]
    public async Task QueryLoggingSettings_PatchPrivacyLevelAndRetentionPeriod_ReportsAMixedReloadNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage patchResponse = await client.PatchAsync(
            "/api/query-logging-settings",
            JsonContent.Create(
                new { privacyLevel = "DomainRedacted", retentionPeriod = "7.00:00:00" }
            )
        );
        patchResponse.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await patchResponse.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("privacyLevel", result.Note, StringComparison.Ordinal);
        Assert.Equal(RuntimeSettingsWriteResult.QueryLoggingMixedReloadNote, result.Note);
    }

    [Fact]
    public async Task QueryLoggingSettings_PatchAnInvalidPrivacyLevel_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage patchResponse = await client.PatchAsync(
            "/api/query-logging-settings",
            JsonContent.Create(new { privacyLevel = "NotARealLevel" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
    }
}

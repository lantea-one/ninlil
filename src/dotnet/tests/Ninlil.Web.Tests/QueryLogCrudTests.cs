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
///     <see cref="QueryLoggingSettingsController" /> end to end, against a real, in-process host.
///     Its own most important coverage is a direct regression test for a real bug found while
///     integrating the Angular admin SPA against this already-built controller: a query-string
///     enum filter (<c>responseCode</c>/<c>queriedType</c>) must accept a value spelled either as
///     the enum's own C# member name or as the wire-format string a response body actually
///     renders that same value as -- the two differ by more than casing for several
///     <see cref="Ninlil.Dns.Common.Enum.DnsResponseCodeEnum" /> members, so accepting only
///     the member name would silently reject the exact value a caller had just read back out of
///     an earlier search result.
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
    // "FORMERR"/"SERVFAIL"/"NOTIMP" are DnsResponseCodeEnum's own [EnumMember] wire strings --
    // the exact text a response body actually renders, and letters that genuinely differ (not
    // just casing) from the C# member name below.
    [InlineData("FORMERR")]
    [InlineData("SERVFAIL")]
    [InlineData("NOTIMP")]
    // "FormatError"/"ServerFailure"/"NotImplemented" are the same three values spelled as their
    // own literal C# member name instead.
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
    ///     This test confirms a write touching only <c>privacyLevel</c> reports this project's own
    ///     "no restart required" note. It deliberately does not also assert the following
    ///     <c>GET</c> reflects the new value -- see <see cref="AppearanceSettingsControllerTests" />'s
    ///     own doc comment for why this shared HTTP fixture's own isolated runtime settings file
    ///     is never wired back in as a real, reload-on-change configuration source the way a real
    ///     deployment's <c>appsettings.json</c> is; <see cref="QueryLoggingOptionsHotReloadTests" />
    ///     is the dedicated test that actually proves the live reload happens, end to end, against
    ///     the real .NET configuration/options binding pipeline.
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
    ///     This test confirms a write touching both <c>privacyLevel</c> (hot-reloading) and
    ///     <c>retentionPeriod</c> (restart-required) reports the mixed-reload note naming both --
    ///     unlike <see cref="QueryLoggingSettings_PatchPrivacyLevelOnly_ReportsNoRestartRequired" />,
    ///     which touches only the hot-reloading field.
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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);
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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the rule that the records API only accepts an owner name that is the
///     zone's own name or below it, over HTTP on the shared in-process host, signed in as the
///     bootstrap administrator. A name such as "www" would be stored as written and never
///     answered, so create, PUT and PATCH refuse it with 400 and a message that names the zone and
///     suggests the full name. The comparison is label-wise and case-insensitive (RFC 4343), so
///     "wwwexample.test" is refused although it ends with the zone's text. Each test creates its
///     own uniquely named zone, so the shared host's other data does not matter.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RecordNameInZoneTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>
    ///     This method creates a Primary zone named <c>{prefix}-{guid}.test</c> with its SOA at the
    ///     apex and returns its name and identifier.
    /// </summary>
    private static async Task<(string Name, Guid Id)> CreateZoneWithSoaAsync(
        HttpClient client,
        string prefix
    )
    {
        string zoneName = $"{prefix}-{Guid.NewGuid():N}.test";
        HttpResponseMessage zoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, zoneResponse.StatusCode);
        ZoneDetail zone = await zoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();

        HttpResponseMessage soaResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = "@",
                type = "SOA",
                timeToLiveSeconds = 3600,
                data = new
                {
                    primaryNameServer = $"ns1.{zoneName}",
                    responsiblePersonMailbox = $"admin.{zoneName}",
                    serial = 1,
                    refreshSeconds = 3600,
                    retrySeconds = 600,
                    expireSeconds = 604800,
                    minimumTtlSeconds = 300,
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, soaResponse.StatusCode);
        return (zoneName, zone.Id);
    }

    /// <summary>
    ///     This method builds an A record request body with the given owner name.
    /// </summary>
    private static object ABody(string name) =>
        new
        {
            name,
            type = "A",
            timeToLiveSeconds = 300,
            data = new { address = "192.0.2.10" },
        };

    /// <summary>
    ///     This method sends a PATCH with a JSON merge-patch body.
    /// </summary>
    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    /// <summary>
    ///     This method reads the problem details "detail" text of a response.
    /// </summary>
    private static async Task<string> ReadDetailAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("detail").GetString();
    }

    /// <summary>
    ///     A name with no zone suffix is refused with 400, and the message names the zone and
    ///     suggests the full name.
    /// </summary>
    [Fact]
    public async Task Create_ANameWithoutTheZoneSuffix_Returns400WithTheFullNameSuggestion()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-short");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody("www")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"'www' is not in zone '{zoneName}'; did you mean 'www.{zoneName}'?",
            await ReadDetailAsync(response)
        );
    }

    /// <summary>
    ///     A name in another zone's namespace is refused with 400.
    /// </summary>
    [Fact]
    public async Task Create_ANameInAnotherZone_Returns400()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (_, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-other");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody("www.other.test")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     A name that ends with the zone's text but not on a label boundary is not below the zone.
    /// </summary>
    [Fact]
    public async Task Create_ANameThatOnlyEndsWithTheZoneText_Returns400()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-glued");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody($"www{zoneName}")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     A name that is not a valid DNS name, such as a Unicode label below the zone, is refused
    ///     with 400 and a message asking for its xn-- form, never a zone-suffix suggestion.
    /// </summary>
    [Fact]
    public async Task Create_AMalformedName_Returns400WithoutASuffixSuggestion()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-bad");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody($"ü.{zoneName}")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string detail = await ReadDetailAsync(response);
        Assert.Contains("is not a valid DNS name", detail);
        Assert.DoesNotContain("did you mean", detail);
    }

    /// <summary>
    ///     The zone's own name, "@", a name below the zone and a differently cased name below the
    ///     zone are all stored (RFC 4343: names compare case-insensitively).
    /// </summary>
    [Fact]
    public async Task Create_ANameAtOrBelowTheZone_IsStored()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-ok");

        foreach (
            string name in new[]
            {
                $"www.{zoneName}",
                "@",
                zoneName,
                $"WWW.{zoneName.ToUpperInvariant()}",
            }
        )
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/zones/{zoneId}/records",
                ABody(name)
            );
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    /// <summary>
    ///     PUT refuses a name outside the zone with 400 and leaves the record unchanged.
    /// </summary>
    [Fact]
    public async Task Replace_ANameWithoutTheZoneSuffix_Returns400AndKeepsTheName()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-put");
        RecordDetail created = await (
            await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", ABody($"h.{zoneName}"))
        ).Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/zones/{zoneId}/records/{created.Id}",
            ABody("www")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"did you mean 'www.{zoneName}'?", await ReadDetailAsync(response));
        RecordDetail fetched = await client.GetFromJsonAsync<RecordDetail>(
            $"/api/zones/{zoneId}/records/{created.Id}"
        );
        Assert.Equal($"h.{zoneName}", fetched.Name);
    }

    /// <summary>
    ///     PATCH refuses a name outside the zone with 400 and leaves the record unchanged.
    /// </summary>
    [Fact]
    public async Task Patch_ANameWithoutTheZoneSuffix_Returns400AndKeepsTheName()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-patch");
        RecordDetail created = await (
            await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", ABody($"h.{zoneName}"))
        ).Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest($"/api/zones/{zoneId}/records/{created.Id}", new { name = "www" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"did you mean 'www.{zoneName}'?", await ReadDetailAsync(response));
        RecordDetail fetched = await client.GetFromJsonAsync<RecordDetail>(
            $"/api/zones/{zoneId}/records/{created.Id}"
        );
        Assert.Equal($"h.{zoneName}", fetched.Name);
    }

    /// <summary>
    ///     PUT and PATCH accept a full name below the zone.
    /// </summary>
    [Fact]
    public async Task ReplaceAndPatch_AFullNameBelowTheZone_AreAccepted()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateZoneWithSoaAsync(client, "rn-edit-ok");
        RecordDetail created = await (
            await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", ABody($"h.{zoneName}"))
        ).Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage put = await client.PutAsJsonAsync(
            $"/api/zones/{zoneId}/records/{created.Id}",
            ABody($"a.{zoneName}")
        );
        HttpResponseMessage patch = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{zoneId}/records/{created.Id}",
                new { name = $"b.{zoneName}" }
            )
        );

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
    }
}

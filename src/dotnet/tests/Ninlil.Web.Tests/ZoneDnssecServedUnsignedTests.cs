using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>ZoneDetail.DnssecServedUnsigned</c> over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator. The flag comes from the host's real signing
///     cache, which signs in the background after a zone or key change, so the tests poll the zone
///     with a bounded wait instead of reading it once. They check a signed zone with a key
///     (false), the same zone after its only key is retired (true), a signed zone with no key
///     (true), and an unsigned Primary zone and a Secondary zone (false).
/// </summary>
[Collection(WebTestCollection.Name)]
public class ZoneDnssecServedUnsignedTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant is how long a test waits for the signing cache to catch up with a change.
    /// </summary>
    private static readonly TimeSpan CacheTimeout = TimeSpan.FromSeconds(30);

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
    ///     This method creates a zone from an anonymous request body and returns its detail.
    /// </summary>
    private static async Task<ZoneDetail> CreateZoneAsync(HttpClient client, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/zones", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<ZoneDetail>();
    }

    /// <summary>
    ///     This method reads a zone again, so the flag reflects the cache at this moment.
    /// </summary>
    private static async Task<ZoneDetail> GetZoneAsync(HttpClient client, Guid zoneId) =>
        await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{zoneId}");

    /// <summary>
    ///     This method polls the zone until its flag has the wanted value, or fails the test after
    ///     <see cref="CacheTimeout" />.
    /// </summary>
    private static async Task WaitForFlagAsync(HttpClient client, Guid zoneId, bool expected)
    {
        DateTime deadline = DateTime.UtcNow.Add(CacheTimeout);
        while (DateTime.UtcNow < deadline)
        {
            if ((await GetZoneAsync(client, zoneId)).DnssecServedUnsigned == expected)
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Fail(
            $"The zone's dnssecServedUnsigned flag never became {expected} within {CacheTimeout}."
        );
    }

    /// <summary>
    ///     This method marks a zone signed through a merge patch, which triggers the cache reload.
    /// </summary>
    private static async Task MarkSignedAsync(HttpClient client, Guid zoneId)
    {
        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/zones/{zoneId}")
            {
                Content = new StringContent(
                    """{ "isDnssecSigned": true, "dnssecAlgorithm": "ED25519" }""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Get_ForASignedPrimaryZoneWithAKey_ReportsNotServedUnsignedUntilTheKeyIsRetired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(
            client,
            new { name = $"served-{Guid.NewGuid():N}.example.com", type = "Primary" }
        );
        HttpResponseMessage keyResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new { role = "ZSK", algorithm = "ED25519" }
        );
        Assert.Equal(HttpStatusCode.Created, keyResponse.StatusCode);
        DnssecKeyDetail key = await keyResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();

        // The zone is not marked signed yet, so the flag is false whatever the cache holds.
        Assert.False((await GetZoneAsync(client, zone.Id)).DnssecServedUnsigned);

        await MarkSignedAsync(client, zone.Id);
        await WaitForFlagAsync(client, zone.Id, expected: false);

        HttpResponseMessage retire = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{key.Id}",
            new { isRetired = true, comments = "retired by test" }
        );
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        await WaitForFlagAsync(client, zone.Id, expected: true);
    }

    [Fact]
    public async Task Get_ForASignedPrimaryZoneWithNoKey_ReportsServedUnsigned()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(
            client,
            new { name = $"nokey-{Guid.NewGuid():N}.example.com", type = "Primary" }
        );

        await MarkSignedAsync(client, zone.Id);

        await WaitForFlagAsync(client, zone.Id, expected: true);
    }

    [Fact]
    public async Task List_ReportsTheFlagForEachZone()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail unsigned = await CreateZoneAsync(
            client,
            new { name = $"listed-{Guid.NewGuid():N}.example.com", type = "Primary" }
        );
        ZoneDetail noKey = await CreateZoneAsync(
            client,
            new { name = $"listed-nokey-{Guid.NewGuid():N}.example.com", type = "Primary" }
        );
        await MarkSignedAsync(client, noKey.Id);
        await WaitForFlagAsync(client, noKey.Id, expected: true);

        // The shared host holds other tests' zones, so page through until both are found.
        Dictionary<Guid, bool> flags = [];
        for (int skip = 0; flags.Count < 2; skip += 100)
        {
            JsonElement page = await client.GetFromJsonAsync<JsonElement>(
                $"/api/zones?skip={skip}&take=100"
            );
            JsonElement items = page.GetProperty("items");
            if (items.GetArrayLength() == 0)
                break;

            foreach (JsonElement item in items.EnumerateArray())
            {
                Guid id = item.GetProperty("id").GetGuid();
                if (id == unsigned.Id || id == noKey.Id)
                    flags[id] = item.GetProperty("dnssecServedUnsigned").GetBoolean();
            }
        }

        Assert.False(flags[unsigned.Id]);
        Assert.True(flags[noKey.Id]);
    }

    [Fact]
    public async Task Get_ForAnUnsignedPrimaryZone_ReportsNotServedUnsigned()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(
            client,
            new { name = $"plain-{Guid.NewGuid():N}.example.com", type = "Primary" }
        );

        Assert.False(zone.DnssecServedUnsigned);
        Assert.False((await GetZoneAsync(client, zone.Id)).DnssecServedUnsigned);
    }

    [Fact]
    public async Task Get_ForASecondaryZone_NeverReportsServedUnsigned()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(
            client,
            new
            {
                name = $"secondary-{Guid.NewGuid():N}.example.com",
                type = "Secondary",
                primaryServerAddresses = new[] { "192.0.2.10" },
                isDnssecSigned = true,
            }
        );

        // A secondary's DNSSEC data arrives by transfer, so it has no key to lack.
        Assert.True(zone.IsDnssecSigned);
        Assert.False(zone.DnssecServedUnsigned);
        Assert.False((await GetZoneAsync(client, zone.Id)).DnssecServedUnsigned);
    }
}

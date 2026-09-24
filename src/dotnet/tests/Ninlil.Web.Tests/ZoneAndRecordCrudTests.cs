using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the zone and record CRUD REST endpoints end-to-end, against a
///     real, in-process host and a real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />)
///     -- creating a zone, bootstrapping it with its own SOA record, adding and editing an
///     ordinary record via both PUT (full replace) and PATCH (RFC 7396 JSON Merge Patch), and
///     deleting both a record and the zone itself. This is also the regression test for
///     <c>IZoneUpdateService.ApplyRecordEditAsync</c>'s own REST-facing contract: a record's own
///     identifier must survive a PUT/PATCH edit.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ZoneAndRecordCrudTests(NinlilDnsWebApplicationFactory factory)
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

    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    [Fact]
    public async Task ZoneAndRecordLifecycle_CreateEditAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"crud-{Guid.NewGuid():N}.example.com";

        // Create the zone's own metadata shell.
        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
        ZoneDetail zone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal(zoneName, zone.Name);

        // Creating a second zone with the same name must be rejected.
        HttpResponseMessage duplicateZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateZoneResponse.StatusCode);

        // An ordinary record cannot be created before the zone has its own SOA.
        HttpResponseMessage prematureRecordResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"www.{zoneName}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "192.0.2.10" },
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, prematureRecordResponse.StatusCode);

        // Bootstrap the zone with its own SOA record.
        HttpResponseMessage createSoaResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = zoneName,
                type = "SOA",
                timeToLiveSeconds = 3600,
                data = new
                {
                    primaryNameServer = $"ns1.{zoneName}",
                    responsiblePersonMailbox = $"admin.{zoneName}",
                    serial = 100,
                    refreshSeconds = 3600,
                    retrySeconds = 600,
                    expireSeconds = 604800,
                    minimumTtlSeconds = 300,
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, createSoaResponse.StatusCode);

        // A second SOA record must be rejected -- a zone has exactly one.
        HttpResponseMessage secondSoaResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = zoneName,
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
        Assert.Equal(HttpStatusCode.Conflict, secondSoaResponse.StatusCode);

        // Add an ordinary A record.
        HttpResponseMessage createARecordResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"www.{zoneName}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "192.0.2.10" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, createARecordResponse.StatusCode);
        RecordDetail aRecord =
            await createARecordResponse.Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal("192.0.2.10", aRecord.Data.GetProperty("address").GetString());

        // Listing must show both records.
        HttpResponseMessage listRecordsResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records"
        );
        listRecordsResponse.EnsureSuccessStatusCode();
        PagedResult<RecordDetail> recordsPage = await listRecordsResponse.Content.ReadFromJsonAsync<
            PagedResult<RecordDetail>
        >();
        Assert.Equal(2, recordsPage.TotalCount);

        // PUT fully replaces the A record's content, keeping its own identifier.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}",
            new
            {
                name = $"www.{zoneName}",
                type = "A",
                timeToLiveSeconds = 600,
                data = new { address = "192.0.2.20" },
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}"
        );
        RecordDetail replacedRecord =
            await afterReplaceResponse.Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal(aRecord.Id, replacedRecord.Id);
        Assert.Equal(600u, replacedRecord.TimeToLiveSeconds);
        Assert.Equal("192.0.2.20", replacedRecord.Data.GetProperty("address").GetString());

        // PATCH via JSON Merge Patch only touches the field it names.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{zone.Id}/records/{aRecord.Id}",
                new { comments = "patched via merge patch" }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}"
        );
        RecordDetail patchedRecord =
            await afterPatchResponse.Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal("patched via merge patch", patchedRecord.Comments);
        // The field the patch never mentioned must survive untouched.
        Assert.Equal("192.0.2.20", patchedRecord.Data.GetProperty("address").GetString());

        // The zone's own metadata can be merge-patched too.
        HttpResponseMessage zonePatchResponse = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { comments = "zone-level note" })
        );
        Assert.Equal(HttpStatusCode.NoContent, zonePatchResponse.StatusCode);

        HttpResponseMessage afterZonePatchResponse = await client.GetAsync($"/api/zones/{zone.Id}");
        ZoneDetail patchedZone =
            await afterZonePatchResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal("zone-level note", patchedZone.Comments);
        Assert.Equal(zoneName, patchedZone.Name);

        // The zone's own SOA record cannot be deleted directly.
        HttpResponseMessage soaRecordsResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records"
        );
        PagedResult<RecordDetail> allRecords = await soaRecordsResponse.Content.ReadFromJsonAsync<
            PagedResult<RecordDetail>
        >();
        RecordDetail soaRecord = allRecords.Items.Single(record =>
            record.Type == Ninlil.Dns.Common.Enum.DnsResourceRecordTypeEnum.Soa
        );

        HttpResponseMessage deleteSoaResponse = await client.DeleteAsync(
            $"/api/zones/{zone.Id}/records/{soaRecord.Id}"
        );
        Assert.Equal(HttpStatusCode.Conflict, deleteSoaResponse.StatusCode);

        // Deleting the ordinary A record works and is reflected immediately.
        HttpResponseMessage deleteARecordResponse = await client.DeleteAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteARecordResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);

        // Deleting the zone removes it (and its remaining SOA record) entirely.
        HttpResponseMessage deleteZoneResponse = await client.DeleteAsync($"/api/zones/{zone.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteZoneResponse.StatusCode);

        HttpResponseMessage afterDeleteZoneResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteZoneResponse.StatusCode);
    }

    [Fact]
    public async Task Zone_WithCaaSettingsUnset_RoundTripsNullAndDoesNotInventADefault()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"caa-unset-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
        ZoneDetail zone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();

        // A zone that never set either field must round-trip as null (inherit the global
        // CaaAutoManagementOptions default), never a hardcoded value baked into the entity --
        // only CaaRecordReconciliationService's own reconciliation logic resolves that
        // inheritance, never this REST layer.
        Assert.Null(zone.AutoManageCaaRecords);
        Assert.Null(zone.CaaIssuerDomain);

        HttpResponseMessage getResponse = await client.GetAsync($"/api/zones/{zone.Id}");
        ZoneDetail fetched = await getResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Null(fetched.AutoManageCaaRecords);
        Assert.Null(fetched.CaaIssuerDomain);
    }

    [Fact]
    public async Task Zone_WithCaaSettingsSetOnCreateAndReplaceAndPatch_RoundTripsEachTime()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"caa-set-{Guid.NewGuid():N}.example.com";

        // Set both fields explicitly on creation.
        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Primary",
                autoManageCaaRecords = true,
                caaIssuerDomain = "letsencrypt.org",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
        ZoneDetail zone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.True(zone.AutoManageCaaRecords);
        Assert.Equal("letsencrypt.org", zone.CaaIssuerDomain);

        // PUT (full replace) can flip the flag off and change the issuer domain.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}",
            new
            {
                name = zoneName,
                type = "Primary",
                autoManageCaaRecords = false,
                caaIssuerDomain = "pki.goog",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync($"/api/zones/{zone.Id}");
        ZoneDetail afterReplace =
            await afterReplaceResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.False(afterReplace.AutoManageCaaRecords);
        Assert.Equal("pki.goog", afterReplace.CaaIssuerDomain);

        // PATCH (RFC 7396 JSON Merge Patch) can touch just one of the two fields...
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { autoManageCaaRecords = true })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync($"/api/zones/{zone.Id}");
        ZoneDetail afterPatch = await afterPatchResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.True(afterPatch.AutoManageCaaRecords);
        // The field the patch never mentioned must survive untouched.
        Assert.Equal("pki.goog", afterPatch.CaaIssuerDomain);

        // ...and an explicit JSON null clears a field back to "inherit the global default".
        HttpResponseMessage clearPatchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{zone.Id}",
                new { autoManageCaaRecords = (bool?)null, caaIssuerDomain = (string)null }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, clearPatchResponse.StatusCode);

        HttpResponseMessage afterClearResponse = await client.GetAsync($"/api/zones/{zone.Id}");
        ZoneDetail afterClear = await afterClearResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Null(afterClear.AutoManageCaaRecords);
        Assert.Null(afterClear.CaaIssuerDomain);
    }

    [Fact]
    public async Task CreateZone_WithAnInvalidName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // A DNS label may be at most 63 octets -- this one is 64, guaranteed invalid regardless
        // of which characters DnsName.TryParse otherwise tolerates (it deliberately allows most
        // byte values in a label, since the wire format itself imposes no character restriction).
        string invalidName = $"{new string('a', 64)}.example.com";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = invalidName, type = "Primary" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ZoneEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/zones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

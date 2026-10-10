using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the zone and record endpoints over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: creating a zone, giving it its SOA record, adding
///     an ordinary record and editing it with PUT (full replace) and PATCH (RFC 7396 merge patch),
///     the per-zone CAA settings, and deleting a record and the zone. A record must keep its
///     identifier through a PUT or PATCH, which is the contract of
///     <c>IZoneUpdateService.ApplyRecordEditAsync</c> that the API relies on. Changing a record's
///     type to or from SOA, or renaming the SOA, through PUT or PATCH is refused with 409 and
///     leaves the zone able to take new records. A zone whose <c>tsigKeyName</c>
///     names a cluster key (saved directly through the repository) is rejected with 400 on create,
///     PUT and PATCH, while an ordinary key's name is accepted. The per-zone
///     <c>updateTsigKeyNames</c> and <c>transferTsigKeyNames</c> lists round-trip through create,
///     GET, PUT and PATCH, are stored lowercase without duplicates, and reject a cluster key or a
///     malformed name with 400; a name that matches no key is accepted.
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

        // Create the zone, which has no records yet.
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

        // An ordinary record cannot be created before the zone has an SOA.
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

        // Give the zone its SOA record.
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

        // A second SOA record must be rejected; a zone has exactly one.
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

        // PUT replaces the A record's content and keeps its identifier.
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
        // The field the patch did not name must be unchanged.
        Assert.Equal("192.0.2.20", patchedRecord.Data.GetProperty("address").GetString());

        // The zone's metadata can be merge-patched too.
        HttpResponseMessage zonePatchResponse = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { comments = "zone-level note" })
        );
        Assert.Equal(HttpStatusCode.NoContent, zonePatchResponse.StatusCode);

        HttpResponseMessage afterZonePatchResponse = await client.GetAsync($"/api/zones/{zone.Id}");
        ZoneDetail patchedZone =
            await afterZonePatchResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal("zone-level note", patchedZone.Comments);
        Assert.Equal(zoneName, patchedZone.Name);

        // The zone's SOA record cannot be deleted directly.
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

        // An edit cannot turn an ordinary record into an SOA...
        string aRecordUrl = $"/api/zones/{zone.Id}/records/{aRecord.Id}";
        object soaData = new
        {
            primaryNameServer = $"ns1.{zoneName}",
            responsiblePersonMailbox = $"admin.{zoneName}",
            serial = 1,
            refreshSeconds = 3600,
            retrySeconds = 600,
            expireSeconds = 604800,
            minimumTtlSeconds = 300,
        };
        HttpResponseMessage putAsSoaResponse = await client.PutAsJsonAsync(
            aRecordUrl,
            new
            {
                name = aRecord.Name,
                type = "SOA",
                timeToLiveSeconds = 300,
                data = soaData,
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, putAsSoaResponse.StatusCode);

        HttpResponseMessage patchAsSoaResponse = await client.SendAsync(
            PatchRequest(aRecordUrl, new { type = "SOA", data = soaData })
        );
        Assert.Equal(HttpStatusCode.Conflict, patchAsSoaResponse.StatusCode);

        RecordDetail unchangedARecord = await (
            await client.GetAsync(aRecordUrl)
        ).Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal(Ninlil.Dns.Common.Enum.DnsResourceRecordTypeEnum.A, unchangedARecord.Type);

        // ...nor the SOA into another type, nor rename the SOA.
        string soaUrl = $"/api/zones/{zone.Id}/records/{soaRecord.Id}";
        HttpResponseMessage putSoaAsAResponse = await client.PutAsJsonAsync(
            soaUrl,
            new
            {
                name = soaRecord.Name,
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "192.0.2.99" },
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, putSoaAsAResponse.StatusCode);

        HttpResponseMessage patchSoaAsAResponse = await client.SendAsync(
            PatchRequest(soaUrl, new { type = "A", data = new { address = "192.0.2.99" } })
        );
        Assert.Equal(HttpStatusCode.Conflict, patchSoaAsAResponse.StatusCode);

        HttpResponseMessage renameSoaResponse = await client.SendAsync(
            PatchRequest(soaUrl, new { name = $"sub.{zoneName}" })
        );
        Assert.Equal(HttpStatusCode.Conflict, renameSoaResponse.StatusCode);

        RecordDetail unchangedSoa = await (
            await client.GetAsync(soaUrl)
        ).Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal(Ninlil.Dns.Common.Enum.DnsResourceRecordTypeEnum.Soa, unchangedSoa.Type);
        Assert.Equal(soaRecord.Name, unchangedSoa.Name);

        // The SOA is intact, so the zone still accepts a new record.
        HttpResponseMessage laterAddResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"later.{zoneName}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "192.0.2.77" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, laterAddResponse.StatusCode);

        // Deleting the ordinary A record works and is reflected immediately.
        HttpResponseMessage deleteARecordResponse = await client.DeleteAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteARecordResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);

        // Deleting the zone removes it. (Removal of its SOA record is not checked.)
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

        // A zone that never set either field must return null (inherit the global
        // CaaAutoManagementOptions default), not a value stored in the entity; only
        // CaaRecordReconciliationService resolves the inheritance.
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
        // The field the patch did not name must be unchanged.
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
    public async Task Zone_ValidateDnssecSwitch_DefaultsOffAndRoundTripsThroughCreateReplaceAndPatch()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"validate-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Forwarder",
                forwarderAddresses = new[] { "192.0.2.53" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ZoneDetail zone = await createResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.False(zone.ValidateDnssec);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await client.PutAsJsonAsync(
                    $"/api/zones/{zone.Id}",
                    new
                    {
                        name = zoneName,
                        type = "Forwarder",
                        forwarderAddresses = new[] { "192.0.2.53" },
                        validateDnssec = true,
                    }
                )
            ).StatusCode
        );
        Assert.True(
            (await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{zone.Id}")).ValidateDnssec
        );

        // A PATCH that does not name the field leaves it alone.
        await client.SendAsync(PatchRequest($"/api/zones/{zone.Id}", new { comments = "x" }));
        Assert.True(
            (await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{zone.Id}")).ValidateDnssec
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await client.SendAsync(
                    PatchRequest($"/api/zones/{zone.Id}", new { validateDnssec = false })
                )
            ).StatusCode
        );
        Assert.False(
            (await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{zone.Id}")).ValidateDnssec
        );
    }

    [Fact]
    public async Task Zone_ValidateDnssecSwitch_SetOnCreate_IsReturned()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = $"validate-on-{Guid.NewGuid():N}.example.com",
                type = "Forwarder",
                forwarderAddresses = new[] { "192.0.2.53" },
                validateDnssec = true,
            }
        );

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.True((await createResponse.Content.ReadFromJsonAsync<ZoneDetail>()).ValidateDnssec);
    }

    [Fact]
    public async Task CreateRecord_WithACnameTargetThatHasAnEmptyLabel_ReturnsBadRequestAndStoresNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"badname-{Guid.NewGuid():N}.example.com";
        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
        ZoneDetail zone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();
        HttpResponseMessage soaResponse = await client.PostAsJsonAsync(
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
        Assert.Equal(HttpStatusCode.Created, soaResponse.StatusCode);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"alias.{zoneName}",
                type = "CNAME",
                timeToLiveSeconds = 300,
                data = new { target = "a..b." },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        PagedResult<RecordDetail> page = await (
            await client.GetAsync($"/api/zones/{zone.Id}/records")
        ).Content.ReadFromJsonAsync<PagedResult<RecordDetail>>();
        // Only the SOA exists.
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task CreateZone_WithAnInvalidName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // A DNS label may be at most 63 octets, so a 64-octet label is invalid whatever
        // characters DnsName.TryParse accepts (the wire format restricts none).
        string invalidName = $"{new string('a', 64)}.example.com";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = invalidName, type = "Primary" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zone_WithADnssecAlgorithmProhibitedForSigning_IsRejectedOnCreateReplaceAndPatch()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"prohibited-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage rejectedCreate = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Primary",
                dnssecAlgorithm = "DSA",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, rejectedCreate.StatusCode);

        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Primary",
                dnssecAlgorithm = "ECDSAP256SHA256",
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        ZoneDetail zone = await create.Content.ReadFromJsonAsync<ZoneDetail>();

        HttpResponseMessage rejectedReplace = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}",
            new
            {
                name = zoneName,
                type = "Primary",
                dnssecAlgorithm = "RSASHA1",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, rejectedReplace.StatusCode);

        HttpResponseMessage rejectedPatch = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { dnssecAlgorithm = "DSA" })
        );
        Assert.Equal(HttpStatusCode.BadRequest, rejectedPatch.StatusCode);

        // The refused writes changed nothing.
        ZoneDetail after = await (
            await client.GetAsync($"/api/zones/{zone.Id}")
        ).Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal(DnssecAlgorithmEnum.EcdsaP256Sha256, after.DnssecAlgorithm);
    }

    [Fact]
    public async Task ZoneEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/zones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zone_WithAClusterKeyAsItsTsigKey_IsRejectedOnCreateReplaceAndPatch(
        bool unusedInvite
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string clusterKeyName = $"cluster-{Guid.NewGuid():N}";
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
                .AddAsync(
                    new TsigKeyEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = clusterKeyName,
                        AlgorithmName = "hmac-sha256",
                        SecretEncrypted = "not-used",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        ClusterNodeId = unusedInvite ? null : Guid.NewGuid(),
                        ClusterInviteExpiresAtUtc = unusedInvite
                            ? DateTimeOffset.UtcNow.AddHours(1)
                            : null,
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        string zoneName = $"cluster-key-{Guid.NewGuid():N}.example.com";
        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Secondary",
                tsigKeyName = clusterKeyName,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        // The upper-case spelling names the same key.
        HttpResponseMessage createUpper = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Secondary",
                tsigKeyName = clusterKeyName.ToUpperInvariant(),
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, createUpper.StatusCode);

        // An ordinary zone can be created, then neither PUT nor PATCH may point it at the key.
        HttpResponseMessage plain = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        plain.EnsureSuccessStatusCode();
        ZoneDetail zone = await plain.Content.ReadFromJsonAsync<ZoneDetail>();

        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}",
            new
            {
                name = zoneName,
                type = "Primary",
                tsigKeyName = clusterKeyName,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, replace.StatusCode);

        HttpResponseMessage patch = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { tsigKeyName = clusterKeyName })
        );
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);

        // An operator key, or a name that matches no key yet, is accepted.
        HttpResponseMessage keyResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"zone-key-{Guid.NewGuid():N}" }
        );
        keyResponse.EnsureSuccessStatusCode();
        TsigKeyCreateResponse operatorKey =
            await keyResponse.Content.ReadFromJsonAsync<TsigKeyCreateResponse>();
        HttpResponseMessage patchOk = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { tsigKeyName = operatorKey.Key.Name })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchOk.StatusCode);
        HttpResponseMessage patchUnknown = await client.SendAsync(
            PatchRequest($"/api/zones/{zone.Id}", new { tsigKeyName = "not-created-yet" })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchUnknown.StatusCode);
    }

    [Fact]
    public async Task Zone_KeyBindingLists_RoundTripThroughCreateReplaceAndPatch()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"bound-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = zoneName,
                type = "Primary",
                updateTsigKeyNames = new[] { "Update-Key.", "update-key", "other-update" },
                transferTsigKeyNames = new[] { "Xfer-Key" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        ZoneDetail created = await create.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal(["update-key", "other-update"], created.UpdateTsigKeyNames);
        Assert.Equal(["xfer-key"], created.TransferTsigKeyNames);

        ZoneDetail fetched = await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{created.Id}");
        Assert.Equal(created.UpdateTsigKeyNames, fetched.UpdateTsigKeyNames);
        Assert.Equal(created.TransferTsigKeyNames, fetched.TransferTsigKeyNames);

        // A PATCH changes only the field it names.
        HttpResponseMessage patch = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{created.Id}",
                new { transferTsigKeyNames = new[] { "B", "C" } }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        fetched = await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{created.Id}");
        Assert.Equal(["update-key", "other-update"], fetched.UpdateTsigKeyNames);
        Assert.Equal(["b", "c"], fetched.TransferTsigKeyNames);

        // A PUT without the lists clears them: an empty list accepts no key.
        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/zones/{created.Id}",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);
        fetched = await client.GetFromJsonAsync<ZoneDetail>($"/api/zones/{created.Id}");
        Assert.Empty(fetched.UpdateTsigKeyNames);
        Assert.Empty(fetched.TransferTsigKeyNames);
    }

    [Fact]
    public async Task Zone_KeyBindingLists_RejectAClusterKeyAndAMalformedName()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string clusterKeyName = $"cluster-bind-{Guid.NewGuid():N}";
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
                .AddAsync(
                    new TsigKeyEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = clusterKeyName,
                        AlgorithmName = "hmac-sha256",
                        SecretEncrypted = "not-used",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        ClusterNodeId = Guid.NewGuid(),
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        string zoneName = $"bound-bad-{Guid.NewGuid():N}.example.com";

        foreach (string field in new[] { "updateTsigKeyNames", "transferTsigKeyNames" })
        {
            Dictionary<string, object> clusterBody = new()
            {
                ["name"] = zoneName,
                ["type"] = "Primary",
                [field] = new[] { clusterKeyName.ToUpperInvariant() },
            };
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/zones", clusterBody)).StatusCode
            );

            Dictionary<string, object> malformedBody = new()
            {
                ["name"] = zoneName,
                ["type"] = "Primary",
                [field] = new[] { "bad..name" },
            };
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/zones", malformedBody)).StatusCode
            );
        }

        HttpResponseMessage plain = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        plain.EnsureSuccessStatusCode();
        ZoneDetail zone = await plain.Content.ReadFromJsonAsync<ZoneDetail>();

        HttpResponseMessage patchCluster = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{zone.Id}",
                new { updateTsigKeyNames = new[] { clusterKeyName } }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, patchCluster.StatusCode);

        // A name that matches no key yet is accepted, as for tsigKeyName.
        HttpResponseMessage patchUnknown = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{zone.Id}",
                new { updateTsigKeyNames = new[] { "not-created-yet" } }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchUnknown.StatusCode);
    }
}

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
///     This class checks the error responses of the administrative controllers over HTTP on the
///     shared in-process host, signed in as the bootstrap administrator. Each test provokes a
///     not-found, conflict, validation or internal error and checks that the response is an
///     RFC 7807 <c>application/problem+json</c> body, written by
///     <see cref="Ninlil.Web.ExceptionHandling.NinlilExceptionHandler" /> from the exception the
///     controller threw, with the right status and the details a caller needs (entity type and
///     identifier, or a message naming the problem). It covers
///     <see cref="Controllers.ZonesController" />, <see cref="Controllers.TsigKeysController" />,
///     <see cref="Controllers.DnssecKeysController" />, <see cref="Controllers.CloudProviderCredentialsController" />,
///     <see cref="Controllers.RecordsController" />, <see cref="Controllers.FilterListsController" />,
///     <see cref="Controllers.FilterListEntriesController" />, <see cref="Controllers.ClientGroupsController" />,
///     <see cref="Controllers.ClientGroupNetworksController" />,
///     <see cref="Controllers.ClientGroupListAssignmentsController" />,
///     <see cref="Controllers.ForwardersController" />, <see cref="Controllers.WebhooksController" />,
///     <see cref="Controllers.PresetsController" />, <see cref="Controllers.EventBrokerSettingsController" />,
///     <see cref="Controllers.RateLimitingSettingsController" />,
///     <see cref="Controllers.DnsCookieSettingsController" />,
///     <see cref="Controllers.DnsRewritesController" />, <see cref="Controllers.QueryLogController" />,
///     <see cref="Controllers.BackupController" /> and
///     <see cref="Controllers.ReverseProxySettingsController" />. <see cref="IdentityErrorResponseTests" />
///     does the same for the identity and authentication controllers.
/// </summary>
[Collection(WebTestCollection.Name)]
public class AdministrativeErrorResponseTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<JsonElement> ParseBodyAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(content).RootElement.Clone();
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
    public async Task GetZone_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingZoneId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/zones/{missingZoneId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("Zone", body.GetProperty("entityType").GetString());
        Assert.Equal(missingZoneId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateZone_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"error-response-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(zoneName, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateZone_WithAnInvalidName_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // A DNS label may be at most 63 octets, so a 64-octet label is invalid whatever
        // characters DnsName.TryParse accepts (the wire format restricts none), as in
        // ZoneAndRecordCrudTests.
        string invalidName = $"{new string('a', 64)}.example.com";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = invalidName, type = "Primary" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task GetTsigKey_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingKeyId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/tsig-keys/{missingKeyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("TsigKey", body.GetProperty("entityType").GetString());
        Assert.Equal(missingKeyId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateTsigKey_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string keyName = $"error-response-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = keyName }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = keyName }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(keyName, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTsigKey_WithAnUnsupportedAlgorithm_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new
            {
                name = $"error-response-{Guid.NewGuid():N}.example.com",
                algorithmName = "hmac-md5",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Contains(
            "not a supported TSIG algorithm",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    private async Task<(HttpClient Client, ZoneDetail Zone)> CreateZoneAsync()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"error-response-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        createZoneResponse.EnsureSuccessStatusCode();
        ZoneDetail zone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();

        return (client, zone);
    }

    [Fact]
    public async Task ListDnssecKeys_ForANonExistentZone_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingZoneId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/zones/{missingZoneId}/dnssec-keys"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("Zone", body.GetProperty("entityType").GetString());
        Assert.Equal(missingZoneId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task GetDnssecKey_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();
        Guid missingKeyId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{missingKeyId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("DnssecKey", body.GetProperty("entityType").GetString());
        Assert.Equal(missingKeyId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task GenerateDnssecKey_WithAnAlgorithmThisServerCannotSignWith_Returns400WithValidationProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        // Algorithm 12 (ECC-GOST) is IANA-registered, prohibited for signing and left out of
        // DnssecAlgorithmRegistry (see its summary), so DnssecKeyManagementService rejects it
        // with a ValidationException.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new { role = "KSK", algorithm = "ECC-GOST" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "prohibited for signing",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetCloudProviderCredential_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingCredentialId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/cloud-provider-credentials/{missingCredentialId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("CloudProviderCredential", body.GetProperty("entityType").GetString());
        Assert.Equal(missingCredentialId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateCloudProviderCredential_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"error-response-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name,
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string> { ["ApiToken"] = "irrelevant" },
            }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name,
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string> { ["ApiToken"] = "irrelevant" },
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(name, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCloudProviderCredential_WithAMalformedCredentialShape_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name = $"error-response-{Guid.NewGuid():N}",
                providerType = "Route53",
                // Route 53 requires both AccessKeyId and SecretAccessKey; only one is supplied.
                credentials = new Dictionary<string, string> { ["AccessKeyId"] = "AKIAEXAMPLE" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "SecretAccessKey",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    private async Task<(HttpClient Client, ZoneDetail Zone)> CreateBootstrappedZoneAsync()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        HttpResponseMessage bootstrapResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = zone.Name,
                type = "SOA",
                timeToLiveSeconds = 3600,
                data = new
                {
                    primaryNameServer = $"ns1.{zone.Name}",
                    responsiblePersonMailbox = $"admin.{zone.Name}",
                    serial = 1,
                    refreshSeconds = 3600,
                    retrySeconds = 600,
                    expireSeconds = 604800,
                    minimumTtlSeconds = 300,
                },
            }
        );
        bootstrapResponse.EnsureSuccessStatusCode();

        return (client, zone);
    }

    [Fact]
    public async Task ListRecords_ForANonExistentZone_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingZoneId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/zones/{missingZoneId}/records");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("Zone", body.GetProperty("entityType").GetString());
        Assert.Equal(missingZoneId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task GetRecord_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();
        Guid missingRecordId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/zones/{zone.Id}/records/{missingRecordId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("Record", body.GetProperty("entityType").GetString());
        Assert.Equal(missingRecordId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateRecord_BeforeTheZoneHasAnSoaRecord_Returns400WithValidationProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"www.{zone.Name}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "203.0.113.9" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "own SOA record",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CreateRecord_ASecondSoaRecord_Returns409WithConflictProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateBootstrappedZoneAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = zone.Name,
                type = "SOA",
                timeToLiveSeconds = 3600,
                data = new
                {
                    primaryNameServer = $"ns2.{zone.Name}",
                    responsiblePersonMailbox = $"admin.{zone.Name}",
                    serial = 2,
                    refreshSeconds = 3600,
                    retrySeconds = 600,
                    expireSeconds = 604800,
                    minimumTtlSeconds = 300,
                },
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "already has an SOA record",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CreateRecord_WithDataNotMatchingItsDeclaredType_Returns400WithValidationProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateBootstrappedZoneAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"www.{zone.Name}",
                type = "A",
                timeToLiveSeconds = 300,
                // An A record's data must be a JSON object, so a bare JSON string fails the shape
                // check in RecordDataValidator.EncodeDataOrThrow.
                data = "this is not a valid A record shape",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "does not match the shape a 'A' record expects",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ReplaceRecord_WithDataNotMatchingItsDeclaredType_Returns400WithValidationProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateBootstrappedZoneAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"www.{zone.Name}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "203.0.113.9" },
            }
        );
        createResponse.EnsureSuccessStatusCode();
        RecordDetail record = await createResponse.Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}/records/{record.Id}",
            new
            {
                name = $"www.{zone.Name}",
                type = "A",
                timeToLiveSeconds = 300,
                data = "this is not a valid A record shape",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            replaceResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(replaceResponse);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task DeleteRecord_TheZonesOwnSoaRecord_Returns409WithConflictProblemDetails()
    {
        (HttpClient client, ZoneDetail zone) = await CreateBootstrappedZoneAsync();

        HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/records?skip=0&take=10"
        );
        listResponse.EnsureSuccessStatusCode();
        JsonElement listBody = await ParseBodyAsync(listResponse);
        JsonElement soaRecord = listBody
            .GetProperty("items")
            .EnumerateArray()
            .First(record => record.GetProperty("type").GetString() == "SOA");
        Guid soaRecordId = soaRecord.GetProperty("id").GetGuid();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/zones/{zone.Id}/records/{soaRecordId}"
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "cannot be deleted directly",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ReplaceRecord_WhenTheZonesOwnSoaRecordIsCorruptedDirectlyInStorage_Returns500WithASpecificProblemDetailsMessage()
    {
        (HttpClient client, ZoneDetail zone) = await CreateBootstrappedZoneAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/records",
            new
            {
                name = $"www.{zone.Name}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "203.0.113.9" },
            }
        );
        createResponse.EnsureSuccessStatusCode();
        RecordDetail aRecord = await createResponse.Content.ReadFromJsonAsync<RecordDetail>();

        // Corrupt the zone's SOA record in storage, bypassing the API's validation. This is the
        // deterministic way to reach the "SOA record that could not be decoded"
        // InvalidOperationException in IZoneUpdateService.LoadSoaAsync without a concurrency
        // race. It exercises the catch-all branch of RecordsController.TranslateZoneUpdateFailure:
        // a broken internal state the request did not cause still gets a specific ProblemDetails
        // body, not the framework's generic unhandled-exception response.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IRecordRepository recordRepository =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
                zone.Id,
                zone.Name,
                DnsResourceRecordTypeEnum.Soa
            );
            RecordEntity soaRecord = Assert.Single(soaRecords);
            // JSON null deserializes to a C# null without throwing, so LoadSoaAsync raises its
            // "could not be decoded" InvalidOperationException. Malformed JSON would raise a
            // JsonException instead, which RecordsController does not catch.
            soaRecord.DataJson = "null";
            recordRepository.Update(soaRecord);
            await unitOfWork.SaveChangesAsync();
        }

        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}/records/{aRecord.Id}",
            new
            {
                name = $"www.{zone.Name}",
                type = "A",
                timeToLiveSeconds = 300,
                data = new { address = "203.0.113.10" },
            }
        );

        Assert.Equal(HttpStatusCode.InternalServerError, replaceResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            replaceResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(replaceResponse);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "could not be decoded",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetFilterList_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingListId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/filter-lists/{missingListId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("FilterList", body.GetProperty("entityType").GetString());
        Assert.Equal(missingListId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateFilterList_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"error-response-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name,
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name,
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(name, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFilterListEntry_AgainstASubscribedList_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"error-response-subscribed-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Subscribed",
                subscriptionUrl = "https://example.invalid/hosts.txt",
            }
        );
        listResponse.EnsureSuccessStatusCode();
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        // A manual write to a Subscribed list is rejected, because the next refresh replaces
        // the whole entry set and would silently discard the edit.
        HttpResponseMessage entryResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "manual.example.com" }
        );

        Assert.Equal(HttpStatusCode.Conflict, entryResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            entryResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(entryResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "subscribed list",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CreateFilterListEntry_WithAMalformedRegexPattern_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"error-response-regex-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        listResponse.EnsureSuccessStatusCode();
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage entryResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "(unclosed", kind = "Regex" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, entryResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            entryResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(entryResponse);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "not a valid regular expression",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetClientGroup_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingGroupId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/client-groups/{missingGroupId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("ClientGroup", body.GetProperty("entityType").GetString());
        Assert.Equal(missingGroupId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateClientGroup_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"error-response-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(name, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateClientGroupNetwork_WithAMalformedCidr_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"error-response-{Guid.NewGuid():N}" }
        );
        groupResponse.EnsureSuccessStatusCode();
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();

        HttpResponseMessage networkResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/networks",
            new { cidr = "not-a-real-network" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, networkResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            networkResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(networkResponse);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task CreateClientGroupListAssignment_ADuplicate_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"error-response-{Guid.NewGuid():N}" }
        );
        groupResponse.EnsureSuccessStatusCode();
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"error-response-assign-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        listResponse.EnsureSuccessStatusCode();
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage firstAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/list-assignments",
            new { listId = list.Id }
        );
        firstAssignmentResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/list-assignments",
            new { listId = list.Id }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateAssignmentResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateAssignmentResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(duplicateAssignmentResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(
            list.Id.ToString(),
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetForwarder_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingForwarderId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/forwarders/{missingForwarderId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("Forwarder", body.GetProperty("entityType").GetString());
        Assert.Equal(missingForwarderId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateForwarder_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"error-response-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/forwarders",
            new { name, definitionString = "203.0.113.53" }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/forwarders",
            new { name, definitionString = "203.0.113.54" }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(name, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateForwarder_WithAMalformedDefinitionString_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/forwarders",
            new
            {
                name = $"error-response-{Guid.NewGuid():N}",
                definitionString = "not a real forwarder definition at all!!",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "not a valid forwarder definition",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetWebhook_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingWebhookId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync($"/api/webhooks/{missingWebhookId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("WebhookSubscription", body.GetProperty("entityType").GetString());
        Assert.Equal(missingWebhookId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateWebhook_WithADuplicateName_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"error-response-{Guid.NewGuid():N}";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name,
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "DnsZoneChanged" },
            }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name,
                targetUrl = "https://example.invalid/other",
                eventTypeNames = new[] { "DnsCacheEvicted" },
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(name, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWebhook_WithAMalformedTargetUrl_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name = $"error-response-{Guid.NewGuid():N}",
                targetUrl = "not-a-url-at-all",
                eventTypeNames = new[] { "DnsZoneChanged" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "not a valid absolute",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CreateWebhook_WithAnUnknownEventTypeName_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name = $"error-response-{Guid.NewGuid():N}",
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "NotARealEventTypeAtAll" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "is not a registered event type",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ApplyPreset_ForAnUnknownName_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            "/api/presets/NotARealPresetName/apply",
            content: null
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("Preset", body.GetProperty("entityType").GetString());
        Assert.Equal("NotARealPresetName", body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task PatchEventBrokerSettings_WithAnInvalidTransport_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/event-broker-settings", new { transport = "NotARealTransport" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "is not a valid transport",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PatchEventBrokerSettings_WithAMalformedRabbitMqTimeSpan_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/event-broker-settings",
                new { rabbitMq = new { initialReconnectDelay = "not-a-timespan" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "must be a valid TimeSpan",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PatchRateLimitingSettings_WithAMalformedTrackedClientExpiry_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/rate-limiting-settings",
                new { trackedClientExpiry = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "'trackedClientExpiry' must be a valid TimeSpan",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PatchDnsCookieSettings_WithAMalformedSecretRotationInterval_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dns-cookie-settings",
                new { secretRotationInterval = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "'secretRotationInterval' must be a valid TimeSpan",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetDnsRewrite_ForANonExistentId_Returns404WithNotFoundProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid missingRewriteId = Guid.NewGuid();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/dns-rewrites/{missingRewriteId}"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal("DnsRewrite", body.GetProperty("entityType").GetString());
        Assert.Equal(missingRewriteId.ToString(), body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateDnsRewrite_WithADuplicateDomainAndTargetKind_Returns409WithConflictProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string domain = $"error-response-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain, target = "203.0.113.60" }
        );
        firstResponse.EnsureSuccessStatusCode();

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain, target = "203.0.113.61" }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(duplicateResponse);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Contains(domain, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDnsRewrite_WithAMalformedDomain_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // A DNS label may not exceed 63 octets (DnsName.MaximumLabelLength), so a 300-character
        // label is invalid. A stray punctuation character would not be, since DNS restricts no
        // characters in a label.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain = new string('a', 300), target = "203.0.113.62" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "is not a valid domain name",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task SearchQueryLog_WithAMalformedResponseCode_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/query-log?responseCode=NotARealResponseCodeAtAll"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "is not a valid",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RestoreBackup_WithTheWrongPassphrase_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage backupResponse = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("the-real-error-response-passphrase")
        );
        backupResponse.EnsureSuccessStatusCode();
        byte[] archive = await backupResponse.Content.ReadAsByteArrayAsync();

        MultipartFormDataContent restoreContent = new();
        ByteArrayContent archiveContent = new(archive);
        archiveContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        restoreContent.Add(archiveContent, "Archive", "backup.ltbk");
        restoreContent.Add(new StringContent("a-completely-different-passphrase"), "Passphrase");

        HttpResponseMessage restoreResponse = await client.PostAsync(
            "/api/backup/restore",
            restoreContent
        );

        // BackupDecryptionFailedException belongs to Server.Backup, outside the Ninlil exception
        // hierarchy, and BackupController translates it into a ValidationException. This test
        // checks that translation; BackupCrudTests covers the other rejections (the database
        // left untouched after a wrong passphrase, a too-short passphrase, a missing archive).
        Assert.Equal(HttpStatusCode.BadRequest, restoreResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            restoreResponse.Content.Headers.ContentType?.MediaType
        );

        JsonElement body = await ParseBodyAsync(restoreResponse);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task PatchReverseProxySettings_WithAMalformedKnownProxyAddress_Returns400WithValidationProblemDetails()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { knownProxies = new[] { "not-a-real-ip-address" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await ParseBodyAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Contains(
            "not a valid IP address",
            body.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );
    }
}

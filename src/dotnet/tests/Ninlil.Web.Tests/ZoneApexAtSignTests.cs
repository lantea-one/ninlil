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
///     This class covers how the records API treats the owner name "@" over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. RFC 1035 §5.1 says "A free
///     standing @ is used to denote the current origin", so "@" on create, PUT and PATCH must be
///     stored as the zone's own name, for the SOA and for every other type. A zone whose SOA is
///     stored under another name has no SOA at its apex, so the API refuses to create such a first
///     record (400), and a zone that has records but no apex SOA answers 409 rather than 500 when
///     a record is added. Each test creates its own zone, so the shared host's other data does not
///     matter.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ZoneApexAtSignTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>
    ///     This method creates a Primary zone with no records and returns its name and identifier.
    /// </summary>
    private static async Task<(string Name, Guid Id)> CreateEmptyZoneAsync(
        HttpClient client,
        string prefix
    )
    {
        string zoneName = $"{prefix}-{Guid.NewGuid():N}.example.com";
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ZoneDetail zone = await response.Content.ReadFromJsonAsync<ZoneDetail>();
        return (zoneName, zone.Id);
    }

    /// <summary>
    ///     This method builds an SOA request body whose owner name is <paramref name="name" />.
    /// </summary>
    private static object SoaBody(string name, string zoneName) =>
        new
        {
            name,
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
        };

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
    ///     An SOA created as "@" is stored under the zone's name, and the zone then accepts a
    ///     further record instead of answering 500 "has no SOA record to increment".
    /// </summary>
    [Fact]
    public async Task CreateSoa_NamedAtSign_StoresTheZoneNameAndTheZoneAcceptsTheNextRecord()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "at-soa");

        HttpResponseMessage soaResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            SoaBody("@", zoneName)
        );
        Assert.Equal(HttpStatusCode.Created, soaResponse.StatusCode);
        RecordDetail soa = await soaResponse.Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal(zoneName, soa.Name);

        HttpResponseMessage aResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody($"www.{zoneName}")
        );
        Assert.Equal(HttpStatusCode.Created, aResponse.StatusCode);
    }

    /// <summary>
    ///     A non-SOA record created as "@" is stored under the zone's name.
    /// </summary>
    [Fact]
    public async Task CreateRecord_NamedAtSign_StoresTheZoneName()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "at-create");
        Assert.Equal(
            HttpStatusCode.Created,
            (
                await client.PostAsJsonAsync(
                    $"/api/zones/{zoneId}/records",
                    SoaBody(zoneName, zoneName)
                )
            ).StatusCode
        );

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody("@")
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        RecordDetail created = await response.Content.ReadFromJsonAsync<RecordDetail>();
        Assert.Equal(zoneName, created.Name);
        RecordDetail fetched = await client.GetFromJsonAsync<RecordDetail>(
            $"/api/zones/{zoneId}/records/{created.Id}"
        );
        Assert.Equal(zoneName, fetched.Name);
    }

    /// <summary>
    ///     PUT with the name "@" stores the zone's name.
    /// </summary>
    [Fact]
    public async Task ReplaceRecord_NamedAtSign_StoresTheZoneName()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "at-put");
        await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", SoaBody(zoneName, zoneName));
        RecordDetail created = await (
            await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", ABody($"h.{zoneName}"))
        ).Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/zones/{zoneId}/records/{created.Id}",
            ABody("@")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RecordDetail fetched = await client.GetFromJsonAsync<RecordDetail>(
            $"/api/zones/{zoneId}/records/{created.Id}"
        );
        Assert.Equal(zoneName, fetched.Name);
    }

    /// <summary>
    ///     PATCH with the name "@" stores the zone's name.
    /// </summary>
    [Fact]
    public async Task PatchRecord_NamedAtSign_StoresTheZoneName()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "at-patch");
        await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", SoaBody(zoneName, zoneName));
        RecordDetail created = await (
            await client.PostAsJsonAsync($"/api/zones/{zoneId}/records", ABody($"h.{zoneName}"))
        ).Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest($"/api/zones/{zoneId}/records/{created.Id}", new { name = "@" })
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RecordDetail fetched = await client.GetFromJsonAsync<RecordDetail>(
            $"/api/zones/{zoneId}/records/{created.Id}"
        );
        Assert.Equal(zoneName, fetched.Name);
    }

    /// <summary>
    ///     Editing the SOA with the name "@" is the same name as its stored apex name, so it is
    ///     not a rename (no 409) and the name stays the zone's name.
    /// </summary>
    [Fact]
    public async Task ReplaceAndPatchSoa_NamedAtSign_AreNotRenames()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "at-soaedit");
        RecordDetail soa = await (
            await client.PostAsJsonAsync(
                $"/api/zones/{zoneId}/records",
                SoaBody(zoneName, zoneName)
            )
        ).Content.ReadFromJsonAsync<RecordDetail>();

        HttpResponseMessage put = await client.PutAsJsonAsync(
            $"/api/zones/{zoneId}/records/{soa.Id}",
            SoaBody("@", zoneName)
        );
        HttpResponseMessage patch = await client.SendAsync(
            PatchRequest($"/api/zones/{zoneId}/records/{soa.Id}", new { name = "@" })
        );

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        RecordDetail fetched = await client.GetFromJsonAsync<RecordDetail>(
            $"/api/zones/{zoneId}/records/{soa.Id}"
        );
        Assert.Equal(zoneName, fetched.Name);
    }

    /// <summary>
    ///     A first SOA that is not at the zone's apex would leave the zone without an SOA that the
    ///     serial logic can find, so it is refused with 400 and nothing is stored.
    /// </summary>
    [Fact]
    public async Task CreateFirstSoa_NotAtTheApex_ReturnsBadRequestAndStoresNothing()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "soa-offapex");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            SoaBody($"sub.{zoneName}", zoneName)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using IServiceScope scope = factory.Services.CreateScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<IRecordRepository>()
                .ListByZoneIdAsync(zoneId)
        );
    }

    /// <summary>
    ///     A zone that holds records but no SOA at its apex (saved here through the repository)
    ///     answers a new record with 409, a state the caller can fix, instead of 500.
    /// </summary>
    [Fact]
    public async Task CreateRecord_InAZoneWithoutAnApexSoa_ReturnsConflict()
    {
        using HttpClient client = await CreateAuthenticatedClientAsync();
        (string zoneName, Guid zoneId) = await CreateEmptyZoneAsync(client, "no-soa");
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IRecordRepository>()
                .AddAsync(
                    new RecordEntity
                    {
                        Id = Guid.NewGuid(),
                        ZoneId = zoneId,
                        Name = $"stray.{zoneName}",
                        Type = DnsResourceRecordTypeEnum.A,
                        Class = DnsClassEnum.Internet,
                        TimeToLiveSeconds = 300,
                        DataJson = "{\"Address\":\"192.0.2.1\"}",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zoneId}/records",
            ABody($"www.{zoneName}")
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

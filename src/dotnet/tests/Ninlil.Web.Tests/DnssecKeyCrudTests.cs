using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the DNSSEC key endpoints over HTTP on the shared in-process host, signed
///     in as the bootstrap administrator: generating a key pair for a zone, keeping private key
///     material out of every response, editing the mutable fields (retirement and comments) with
///     PUT and PATCH, deleting, the DNSKEY and DS records a parent zone needs, and deleting a zone
///     that has keys (see <c>ZonesController.Delete</c>).
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnssecKeyCrudTests(NinlilDnsWebApplicationFactory factory)
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

    private async Task<(HttpClient Client, ZoneDetail Zone)> CreateZoneAsync()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"dnssec-crud-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
        ZoneDetail zone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();

        return (client, zone);
    }

    [Fact]
    public async Task DnssecKeyLifecycle_GenerateEditAndDelete_WorksEndToEnd()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        // Generate a key. Its private key material must never appear in a response.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new
            {
                role = "ZSK",
                algorithm = "RSASHA256",
                keySizeBits = 2048,
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        string createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", createBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkcs8", createBody, StringComparison.OrdinalIgnoreCase);

        DnssecKeyDetail created =
            JsonSerializer.Deserialize<DnssecKeyDetail>(
                createBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? throw new InvalidOperationException("Expected a DNSSEC key detail response.");
        Assert.Equal(zone.Id, created.ZoneId);
        Assert.False(created.IsRetired);
        Assert.False(string.IsNullOrWhiteSpace(created.PublicKeyWireFormat));
        Assert.True(created.KeyTag > 0);

        // Listing must show the key, and must not leak its private key either.
        HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/dnssec-keys"
        );
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkcs8", listBody, StringComparison.OrdinalIgnoreCase);

        // PUT replaces the key's mutable fields.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}",
            new { isRetired = true, comments = "replaced via PUT" }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}"
        );
        DnssecKeyDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();
        Assert.Equal(created.Id, replaced.Id);
        Assert.True(replaced.IsRetired);
        Assert.Equal("replaced via PUT", replaced.Comments);
        // Role, algorithm and key material are fixed; PUT accepts only the mutable fields.
        Assert.Equal(created.Algorithm, replaced.Algorithm);
        Assert.Equal(created.PublicKeyWireFormat, replaced.PublicKeyWireFormat);

        // PATCH via JSON Merge Patch only touches the field it names.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}",
                new { isRetired = false }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}"
        );
        DnssecKeyDetail patched =
            await afterPatchResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();
        Assert.False(patched.IsRetired);
        // The field the patch did not name must be unchanged.
        Assert.Equal("replaced via PUT", patched.Comments);

        // Deleting works and is reflected immediately.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteZone_WithDnssecKeys_RemovesThemToo()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new
            {
                role = "KSK",
                algorithm = "RSASHA256",
                keySizeBits = 2048,
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DnssecKeyDetail created = await createResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();

        HttpResponseMessage deleteZoneResponse = await client.DeleteAsync($"/api/zones/{zone.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteZoneResponse.StatusCode);

        // Get answers 404 for a missing zone as well as a missing key, so this status alone does
        // not show that the key row was removed.
        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task KeyDetail_CarriesTheRecordsAParentZoneNeeds()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        HttpResponseMessage kskResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new { role = "KSK", algorithm = "ECDSAP256SHA256" }
        );
        Assert.Equal(HttpStatusCode.Created, kskResponse.StatusCode);
        DnssecKeyDetail ksk = await kskResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();

        // The digest is recomputed here from RFC 4034 §5.1.4 (the owner name in lowercase wire
        // form, then the DNSKEY RDATA: flags 257, protocol 3, algorithm 13, key) rather than with
        // the server's helpers, so a mistake there cannot hide.
        byte[] publicKey = Convert.FromBase64String(ksk.PublicKeyWireFormat);
        List<byte> input = [];
        foreach (string label in zone.Name.TrimEnd('.').ToLowerInvariant().Split('.'))
        {
            input.Add((byte)label.Length);
            input.AddRange(Encoding.ASCII.GetBytes(label));
        }
        input.Add(0);
        input.AddRange([0x01, 0x01, 3, 13]);
        input.AddRange(publicKey);
        string expectedDigest = Convert.ToHexString(SHA256.HashData([.. input]));

        string owner = zone.Name.TrimEnd('.') + ".";
        Assert.Equal(
            $"{owner} IN DNSKEY 257 3 13 {ksk.PublicKeyWireFormat}",
            ksk.DnsKeyRecord,
            ignoreCase: true
        );
        Assert.Equal(expectedDigest, ksk.DsDigest);
        Assert.Equal(
            $"{owner} IN DS {ksk.KeyTag} 13 2 {expectedDigest}",
            ksk.DsRecord,
            ignoreCase: true
        );

        // The same fields come back from the single-key read. (The list is not checked.)
        DnssecKeyDetail fetched = await client.GetFromJsonAsync<DnssecKeyDetail>(
            $"/api/zones/{zone.Id}/dnssec-keys/{ksk.Id}"
        );
        Assert.Equal(ksk.DsRecord, fetched.DsRecord);

        // A ZSK is never referenced from the parent, so it has a DNSKEY but no DS.
        HttpResponseMessage zskResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new { role = "ZSK", algorithm = "ECDSAP256SHA256" }
        );
        DnssecKeyDetail zsk = await zskResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();
        Assert.StartsWith(
            $"{owner} IN DNSKEY 256 3 13 ",
            zsk.DnsKeyRecord,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Null(zsk.DsRecord);
        Assert.Null(zsk.DsDigest);
    }

    [Theory]
    [InlineData("RSAMD5")]
    [InlineData("DSA")]
    [InlineData("RSASHA1")]
    [InlineData("DSA-NSEC3-SHA1")]
    [InlineData("RSASHA1-NSEC3-SHA1")]
    public async Task CreateDnssecKey_WithAnAlgorithmProhibitedForSigning_ReturnsBadRequest(
        string algorithm
    )
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new { role = "ZSK", algorithm }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "prohibited",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CreateDnssecKey_ForANonExistentZone_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{Guid.NewGuid()}/dnssec-keys",
            new
            {
                role = "ZSK",
                algorithm = "RSASHA256",
                keySizeBits = 2048,
            }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2056)]
    [InlineData(8192)]
    public async Task CreateDnssecKey_ForAnRsaSizeOutsideTheBounds_ReturnsBadRequest(
        int keySizeBits
    )
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new
            {
                role = "ZSK",
                algorithm = "RSASHA256",
                keySizeBits,
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RollDnssecKey_ToAnRsaSizeBelowTheFloor_ReturnsBadRequest()
    {
        (HttpClient client, ZoneDetail zone) = await CreateZoneAsync();

        // An Ed25519 key rolled to RSA exercises the size check without generating a big RSA key.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys",
            new { role = "ZSK", algorithm = "ED25519" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DnssecKeyDetail key = await createResponse.Content.ReadFromJsonAsync<DnssecKeyDetail>();

        HttpResponseMessage rollResponse = await client.PostAsJsonAsync(
            $"/api/zones/{zone.Id}/dnssec-keys/{key.Id}/roll",
            new { algorithm = "RSASHA256", keySizeBits = 512 }
        );

        Assert.Equal(HttpStatusCode.BadRequest, rollResponse.StatusCode);
    }

    [Fact]
    public async Task DnssecKeyEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/zones/{Guid.NewGuid()}/dnssec-keys"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

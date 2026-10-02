using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DnsCacheController" /> over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator, or as a new user with no permissions for
///     the 403 test. Forwarding is off in the test host, so answers are put straight into the
///     host's forwarded cache singleton (the one the forwarding resolver uses) and the tests then
///     remove one name or flush through the API. The recursive cache is listed but never seeded.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnsCacheControllerTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> CreateAuthenticatedClientAsync(
        string userName = null,
        string password = null
    )
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                userName ?? NinlilDnsWebApplicationFactory.AdminUserName,
                password ?? NinlilDnsWebApplicationFactory.AdminPassword
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
    ///     This method signs in as a freshly created user holding no permissions at all.
    /// </summary>
    private async Task<HttpClient> CreateUnprivilegedClientAsync()
    {
        HttpClient adminClient = await CreateAuthenticatedClientAsync();
        string userName = $"dns-cache-test-user-{Guid.NewGuid():N}";
        const string password = "Dns-Cache-Test-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, $"{userName}@localhost.local", "Cache Test")
        );
        createResponse.EnsureSuccessStatusCode();

        return await CreateAuthenticatedClientAsync(userName, password);
    }

    /// <summary>
    ///     This method puts one forwarded answer for <paramref name="name" /> into the host's
    ///     forwarded cache.
    /// </summary>
    private void SeedForwardedAnswer(string name, DnsResourceRecordTypeEnum type)
    {
        IForwardedResponseCache cache =
            factory.Services.GetRequiredService<IForwardedResponseCache>();
        DnsMessage request = new()
        {
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions = { new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet) },
        };
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse(name),
                type,
                DnsClassEnum.Internet,
                300,
                type == DnsResourceRecordTypeEnum.A
                    ? DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.A,
                        new ARecordData(IPAddress.Parse("192.0.2.1"))
                    )
                    : DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Aaaa,
                        new AaaaRecordData(IPAddress.Parse("2001:db8::1"))
                    )
            )
        );

        Assert.True(
            cache.TryStore(
                ForwardedResponseCacheKey.Create(
                    request,
                    request,
                    ["192.0.2.53"],
                    validated: false
                ),
                response
            )
        );
    }

    private static async Task<DnsCacheDetail> GetForwardedAsync(HttpClient client)
    {
        IReadOnlyList<DnsCacheDetail> caches = await client.GetFromJsonAsync<
            IReadOnlyList<DnsCacheDetail>
        >("/api/dns-cache");

        return Assert.Single(caches, cache => cache.Name == "forwarded");
    }

    [Fact]
    public async Task List_ReportsTheForwardedAndRecursiveCaches()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        IReadOnlyList<DnsCacheDetail> caches = await client.GetFromJsonAsync<
            IReadOnlyList<DnsCacheDetail>
        >("/api/dns-cache");

        Assert.Equal(["forwarded", "recursive"], caches.Select(cache => cache.Name));
        Assert.Equal(10000, caches[0].MaxEntries);
        Assert.Equal(100000, caches[1].MaxEntries);
    }

    [Fact]
    public async Task RemoveName_ForgetsOneNameOnly_ThenFlushEmptiesTheCache()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        await client.DeleteAsync("/api/dns-cache?cache=forwarded");

        SeedForwardedAnswer("cache-test.example.com", DnsResourceRecordTypeEnum.A);
        SeedForwardedAnswer("cache-test.example.com", DnsResourceRecordTypeEnum.Aaaa);
        SeedForwardedAnswer("other.example.com", DnsResourceRecordTypeEnum.A);
        Assert.Equal(3, (await GetForwardedAsync(client)).EntryCount);

        HttpResponseMessage removeResponse = await client.DeleteAsync(
            "/api/dns-cache/entries/Cache-Test.Example.com."
        );
        removeResponse.EnsureSuccessStatusCode();
        DnsCacheRemovalResult removed =
            await removeResponse.Content.ReadFromJsonAsync<DnsCacheRemovalResult>();
        Assert.Equal(2, removed.RemovedEntryCount);
        Assert.Equal(1, (await GetForwardedAsync(client)).EntryCount);

        HttpResponseMessage flushResponse = await client.DeleteAsync(
            "/api/dns-cache?cache=forwarded"
        );
        flushResponse.EnsureSuccessStatusCode();
        DnsCacheRemovalResult flushed =
            await flushResponse.Content.ReadFromJsonAsync<DnsCacheRemovalResult>();
        Assert.Equal(1, flushed.RemovedEntryCount);
        Assert.Equal(0, (await GetForwardedAsync(client)).EntryCount);
    }

    [Fact]
    public async Task Flush_WithNoCacheNamed_FlushesEveryCache()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        SeedForwardedAnswer("flush-all.example.com", DnsResourceRecordTypeEnum.A);

        HttpResponseMessage response = await client.DeleteAsync("/api/dns-cache");
        response.EnsureSuccessStatusCode();

        IReadOnlyList<DnsCacheDetail> caches = await client.GetFromJsonAsync<
            IReadOnlyList<DnsCacheDetail>
        >("/api/dns-cache");
        Assert.All(caches, cache => Assert.Equal(0, cache.Hits + cache.Misses));
        Assert.Equal(0, caches.Single(cache => cache.Name == "forwarded").EntryCount);
    }

    [Fact]
    public async Task Flush_OfAnUnknownCache_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.DeleteAsync("/api/dns-cache?cache=nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveName_WithAnInvalidName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/dns-cache/entries/{new string('a', 70)}.example.com"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EveryAction_WithoutTheRecursiveSettingsPermissions_IsForbidden()
    {
        HttpClient client = await CreateUnprivilegedClientAsync();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/dns-cache")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync("/api/dns-cache")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync("/api/dns-cache/entries/example.com")).StatusCode
        );
    }

    [Fact]
    public async Task List_WithoutAToken_ReturnsUnauthorized()
    {
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/dns-cache");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

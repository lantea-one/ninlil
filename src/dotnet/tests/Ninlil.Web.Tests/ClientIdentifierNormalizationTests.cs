using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the normal identifier form on the write endpoints of reservations,
///     clients, client addresses and static address mappings over HTTP on the shared in-process
///     host: any accepted spelling is stored as lowercase colon-separated hex, a second spelling
///     of the same identifier conflicts, and malformed text is a 400. Each test uses
///     identifiers and scopes of its own, because the host's data is shared.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientIdentifierNormalizationTests(NinlilDnsWebApplicationFactory factory)
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
    ///     This method returns six random bytes as the last six octets of a MAC address, so tests
    ///     on the shared host never collide.
    /// </summary>
    private static byte[] RandomMac()
    {
        byte[] mac = Guid.NewGuid().ToByteArray()[..6];
        mac[0] = (byte)(mac[0] | 0x02);
        return mac;
    }

    private static string Spell(byte[] mac, string separator) =>
        string.Join(separator, mac.Select(b => b.ToString("X2")));

    private static string Dotted(byte[] mac) =>
        $"{mac[0]:x2}{mac[1]:x2}.{mac[2]:x2}{mac[3]:x2}.{mac[4]:x2}{mac[5]:x2}";

    private static string Normal(byte[] mac) => string.Join(':', mac.Select(b => b.ToString("x2")));

    private static async Task<DhcpScopeDetail> CreateScopeAsync(
        HttpClient client,
        string protocol,
        string start,
        string end,
        int prefixLength
    )
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"idnorm-{Guid.NewGuid():N}",
                protocolVersion = protocol,
                rangeStartAddress = start,
                rangeEndAddress = end,
                subnetPrefixLength = prefixLength,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpScopeDetail>();
    }

    [Fact]
    public async Task ReservationCreate_WithAHyphenatedUppercaseIdentifier_StoresTheNormalForm()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(
            client,
            "IPv4",
            "192.168.61.100",
            "192.168.61.200",
            24
        );
        byte[] mac = RandomMac();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = Spell(mac, "-"), ipAddress = "192.168.61.101" }
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        DhcpReservationDetail detail =
            await created.Content.ReadFromJsonAsync<DhcpReservationDetail>();
        Assert.Equal(Normal(mac), detail.ClientIdentifier);

        HttpResponseMessage fetched = await client.GetAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations/{detail.Id}"
        );
        DhcpReservationDetail got =
            await fetched.Content.ReadFromJsonAsync<DhcpReservationDetail>();
        Assert.Equal(Normal(mac), got.ClientIdentifier);

        using IServiceScope serviceScope = factory.Services.CreateScope();
        IDhcpReservationRepository repository =
            serviceScope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
        DhcpReservationEntity found = await repository.FindByClientIdentifierAsync(
            scope.Id,
            Normal(mac)
        );
        Assert.NotNull(found);
        Assert.Equal(detail.Id, found.Id);

        await client.DeleteAsync($"/api/dhcp-scopes/{scope.Id}");
    }

    [Fact]
    public async Task ReservationCreate_WithAnotherSpellingOfAReservedIdentifier_Conflicts()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(
            client,
            "IPv4",
            "192.168.62.100",
            "192.168.62.200",
            24
        );
        byte[] mac = RandomMac();

        HttpResponseMessage first = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = Normal(mac), ipAddress = "192.168.62.101" }
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = Dotted(mac), ipAddress = "192.168.62.102" }
        );
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await client.DeleteAsync($"/api/dhcp-scopes/{scope.Id}");
    }

    [Fact]
    public async Task ReservationCreate_WithAMalformedIdentifier_ReturnsBadRequestNamingTheForms()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(
            client,
            "IPv4",
            "192.168.63.100",
            "192.168.63.200",
            24
        );

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = "aa:bb-cc", ipAddress = "192.168.63.101" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("aabb.ccdd.eeff", body);
        Assert.Contains("85 bytes", body);

        await client.DeleteAsync($"/api/dhcp-scopes/{scope.Id}");
    }

    [Fact]
    public async Task ReservationCreateAndReplace_WithAnUnabbreviatedIPv6Address_StoreTheCanonicalForm()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(
            client,
            "IPv6",
            "2001:db8:61::100",
            "2001:db8:61::1ff",
            64
        );

        HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new
            {
                clientIdentifier = "00:03:00:01:" + Spell(RandomMac(), ":"),
                ipAddress = "2001:DB8:61:0:0:0:0:110",
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        DhcpReservationDetail detail =
            await created.Content.ReadFromJsonAsync<DhcpReservationDetail>();
        Assert.Equal("2001:db8:61::110", detail.IpAddress);

        // Another spelling of the same address is the same reservation target.
        HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new
            {
                clientIdentifier = "00:03:00:01:" + Spell(RandomMac(), ":"),
                ipAddress = "2001:db8:61::0110",
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        HttpResponseMessage replaced = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations/{detail.Id}",
            new
            {
                clientIdentifier = detail.ClientIdentifier.ToUpperInvariant(),
                ipAddress = "2001:DB8:61::0:111",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);

        DhcpReservationDetail after = await (
            await client.GetAsync($"/api/dhcp-scopes/{scope.Id}/reservations/{detail.Id}")
        ).Content.ReadFromJsonAsync<DhcpReservationDetail>();
        Assert.Equal("2001:db8:61::111", after.IpAddress);
        Assert.Equal(detail.ClientIdentifier, after.ClientIdentifier);

        await client.DeleteAsync($"/api/dhcp-scopes/{scope.Id}");
    }

    [Fact]
    public async Task ClientCreate_WithAnUppercaseIdentifier_StoresLowercaseAndConflictsOnAnotherSpelling()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        byte[] mac = RandomMac();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = Spell(mac, ":"), protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        ClientDetail detail = await created.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Equal(Normal(mac), Assert.Single(detail.Addresses).ClientIdentifier);

        using (IServiceScope serviceScope = factory.Services.CreateScope())
        {
            IDnsClientRepository repository =
                serviceScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
            DnsClientEntity found = await repository.GetByClientIdentifierAsync(
                Normal(mac),
                InternetProtocolVersionEnum.IPv4
            );
            Assert.NotNull(found);
        }

        HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = Spell(mac, "-"), protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        HttpResponseMessage malformed = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = "aa:bb-cc", protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        await client.DeleteAsync($"/api/clients/{detail.Id}");
    }

    [Fact]
    public async Task ClientAddressCreate_WithADottedIdentifier_StoresTheNormalFormAndConflictsOnAnotherSpelling()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        byte[] ownMac = RandomMac();
        byte[] addedMac = RandomMac();

        HttpResponseMessage owner = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = Normal(ownMac), protocolVersion = "IPv4" }
        );
        ClientDetail ownerDetail = await owner.Content.ReadFromJsonAsync<ClientDetail>();

        HttpResponseMessage added = await client.PostAsJsonAsync(
            $"/api/clients/{ownerDetail.Id}/addresses",
            new { clientIdentifier = Dotted(addedMac), protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        ClientAddressDetail addedDetail =
            await added.Content.ReadFromJsonAsync<ClientAddressDetail>();
        Assert.Equal(Normal(addedMac), addedDetail.ClientIdentifier);

        using (IServiceScope serviceScope = factory.Services.CreateScope())
        {
            IDnsClientAddressRepository repository =
                serviceScope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();
            Assert.NotNull(
                await repository.GetByIdentifierAsync(
                    Normal(addedMac),
                    InternetProtocolVersionEnum.IPv4
                )
            );
        }

        HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            $"/api/clients/{ownerDetail.Id}/addresses",
            new { clientIdentifier = Spell(addedMac, "-"), protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await client.DeleteAsync($"/api/clients/{ownerDetail.Id}");
    }

    [Fact]
    public async Task StaticMappingCreateAndReplace_WithAHyphenatedIdentifier_StoreTheNormalForm()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        byte[] mac = RandomMac();
        byte[] otherMac = RandomMac();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = "203.0.113.77/32",
                clientIdentifier = Spell(mac, "-"),
                protocolVersion = "IPv4",
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        DnsClientStaticAddressMappingDetail detail =
            await created.Content.ReadFromJsonAsync<DnsClientStaticAddressMappingDetail>();
        Assert.Equal(Normal(mac), detail.ClientIdentifier);

        HttpResponseMessage replaced = await client.PutAsJsonAsync(
            $"/api/client-static-address-mappings/{detail.Id}",
            new
            {
                cidr = "203.0.113.77/32",
                clientIdentifier = Spell(otherMac, "-"),
                protocolVersion = "IPv4",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);

        DnsClientStaticAddressMappingDetail after = await (
            await client.GetAsync($"/api/client-static-address-mappings/{detail.Id}")
        ).Content.ReadFromJsonAsync<DnsClientStaticAddressMappingDetail>();
        Assert.Equal(Normal(otherMac), after.ClientIdentifier);

        HttpResponseMessage malformed = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = "203.0.113.78/32",
                clientIdentifier = "aabbc",
                protocolVersion = "IPv4",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        await client.DeleteAsync($"/api/client-static-address-mappings/{detail.Id}");
    }
}

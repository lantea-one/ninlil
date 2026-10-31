using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the two placement rules of the scopes API on the shared host, signed in
///     as the bootstrap administrator: a range must lie inside one subnet of the scope's prefix
///     length, and an enabled scope's range must not share an address with another enabled
///     scope of the same IP version and kind. It checks create, replace and patch, the 400
///     messages, that a disabled scope is never compared, and that a scope stored with a bad
///     range (written through the repository, as a restore would) can still be renamed. Each test
///     takes ranges from <see cref="DhcpTestScopeRanges" /> so it never meets another test's scope.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpScopeRangeRuleTests(NinlilDnsWebApplicationFactory factory)
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

    private static object Body(
        string protocolVersion,
        string start,
        string end,
        int prefixLength,
        bool isDisabled = false,
        string name = null
    ) =>
        new
        {
            name = name ?? $"range-rule-{Guid.NewGuid():N}",
            protocolVersion,
            rangeStartAddress = start,
            rangeEndAddress = end,
            subnetPrefixLength = prefixLength,
            defaultLeaseDuration = "1.00:00:00",
            maxLeaseDuration = "7.00:00:00",
            isDisabled,
        };

    /// <summary>
    ///     This method returns the three leading octets of an IPv4 range from
    ///     <see cref="DhcpTestScopeRanges" />, for building other ranges in the same /24.
    /// </summary>
    private static string Base24(string start) => start[..start.LastIndexOf('.')];

    private static async Task<DhcpScopeDetail> CreateAsync(HttpClient client, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/dhcp-scopes", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpScopeDetail>();
    }

    [Fact]
    public async Task Create_WithAnIPv4RangeThatLeavesItsSubnet_Returns400NamingTheSubnet()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string otherThird = $"{start[..start.LastIndexOf('.', start.LastIndexOf('.') - 1)]}.255.20";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", start, otherThird, prefixLength)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string message = await response.Content.ReadAsStringAsync();
        Assert.Contains($"does not lie inside one /{prefixLength} subnet", message);
        Assert.Contains(start, message);
    }

    [Fact]
    public async Task Create_WithAnIPv6RangeThatLeavesItsSubnet_Returns400NamingTheSubnet()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv6();

        // The range stays in fd00:200::/32 but its end is in the next /64.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv6", start, "fd00:200:fffe::1", prefixLength)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            $"does not lie inside one /{prefixLength} subnet",
            await response.Content.ReadAsStringAsync()
        );
    }

    [Fact]
    public async Task Create_WithARangeThatFillsASmallerSubnetExactly_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        // A /25 covers .0 to .127 of the /24; .0 and .127 are its network and broadcast
        // addresses, so the range ends on the last address before them.
        DhcpScopeDetail created = await CreateAsync(
            client,
            Body("IPv4", $"{baseAddress}.1", $"{baseAddress}.126", 25)
        );

        Assert.Equal($"{baseAddress}.126", created.RangeEndAddress);
    }

    [Fact]
    public async Task Create_WithARangeThatCrossesTheEndOfASmallerSubnet_Returns400()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        // .127 and .128 are in different /25 subnets of the same /24.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.100", $"{baseAddress}.128", 25)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithARangeThatSharesAnAddressWithAnotherEnabledScope_Returns400NamingIt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, string end, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        DhcpScopeDetail first = await CreateAsync(client, Body("IPv4", start, end, prefixLength));
        string baseAddress = Base24(start);

        // The new range starts on the last address of the first one.
        HttpResponseMessage overlapping = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", end, $"{baseAddress}.250", prefixLength)
        );

        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        string message = await overlapping.Content.ReadAsStringAsync();
        Assert.Contains("overlaps scope", message);
        Assert.Contains(first.Name, message);

        // The next address after the first range's end is free.
        DhcpScopeDetail adjacent = await CreateAsync(
            client,
            Body("IPv4", $"{baseAddress}.201", $"{baseAddress}.250", prefixLength)
        );
        Assert.Equal($"{baseAddress}.201", adjacent.RangeStartAddress);
    }

    [Fact]
    public async Task Create_WithARangeThatMatchesOnlyDisabledScopes_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, string end, int prefixLength) = DhcpTestScopeRanges.NextIPv4();

        // A disabled scope answers nobody, so it blocks nothing.
        await CreateAsync(client, Body("IPv4", start, end, prefixLength, isDisabled: true));
        DhcpScopeDetail enabled = await CreateAsync(client, Body("IPv4", start, end, prefixLength));

        // A disabled new scope is not compared either.
        DhcpScopeDetail disabledToo = await CreateAsync(
            client,
            Body("IPv4", start, end, prefixLength, isDisabled: true)
        );

        Assert.False(enabled.IsDisabled);
        Assert.True(disabledToo.IsDisabled);
    }

    [Fact]
    public async Task Create_WithAPrefixDelegationPoolThatSharesAddressesWithAnAddressScope_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, string end, int prefixLength) = DhcpTestScopeRanges.NextIPv6();
        await CreateAsync(client, Body("IPv6", start, end, prefixLength));

        // A delegation pool hands out prefixes, not addresses, so only another delegation pool
        // can collide with it.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"range-rule-pd-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                kind = "PrefixDelegation",
                delegatedPrefixLength = 96,
                rangeStartAddress = start,
                rangeEndAddress = end,
                subnetPrefixLength = prefixLength,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Replace_ToARangeThatOverlapsAnotherScope_Returns400ButTheScopeMayKeepItsOwnRange()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, string end, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);
        DhcpScopeDetail first = await CreateAsync(
            client,
            Body("IPv4", $"{baseAddress}.10", $"{baseAddress}.50", prefixLength)
        );
        DhcpScopeDetail second = await CreateAsync(
            client,
            Body("IPv4", $"{baseAddress}.100", $"{baseAddress}.150", prefixLength)
        );

        // The scope's own range is not an overlap with itself.
        HttpResponseMessage same = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{second.Id}",
            Body(
                "IPv4",
                $"{baseAddress}.100",
                $"{baseAddress}.150",
                prefixLength,
                name: second.Name
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);

        HttpResponseMessage overlapping = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{second.Id}",
            Body("IPv4", $"{baseAddress}.40", $"{baseAddress}.150", prefixLength, name: second.Name)
        );
        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        Assert.Contains(first.Name, await overlapping.Content.ReadAsStringAsync());

        HttpResponseMessage outside = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{second.Id}",
            Body("IPv4", $"{baseAddress}.100", "10.201.0.5", prefixLength, name: second.Name)
        );
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Equal(
            $"{baseAddress}.100",
            (
                await (
                    await client.GetAsync($"/api/dhcp-scopes/{second.Id}")
                ).Content.ReadFromJsonAsync<DhcpScopeDetail>()
            ).RangeStartAddress
        );
    }

    [Fact]
    public async Task Patch_ARangeIntoAnotherScopeOrOutOfTheSubnet_Returns400AndLeavesTheScope()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);
        DhcpScopeDetail first = await CreateAsync(
            client,
            Body("IPv4", $"{baseAddress}.10", $"{baseAddress}.50", prefixLength)
        );
        DhcpScopeDetail second = await CreateAsync(
            client,
            Body("IPv4", $"{baseAddress}.100", $"{baseAddress}.150", prefixLength)
        );

        HttpResponseMessage overlapping = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{second.Id}",
            new { rangeStartAddress = $"{baseAddress}.50" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        Assert.Contains(first.Name, await overlapping.Content.ReadAsStringAsync());

        HttpResponseMessage outside = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{second.Id}",
            new { rangeEndAddress = "10.201.0.150" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Contains("does not lie inside one", await outside.Content.ReadAsStringAsync());

        // A narrower prefix length can also pull the existing range out of its subnet.
        HttpResponseMessage narrower = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{second.Id}",
            new { subnetPrefixLength = 29 }
        );
        Assert.Equal(HttpStatusCode.BadRequest, narrower.StatusCode);

        DhcpScopeDetail after = await (
            await client.GetAsync($"/api/dhcp-scopes/{second.Id}")
        ).Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Equal($"{baseAddress}.100", after.RangeStartAddress);
        Assert.Equal($"{baseAddress}.150", after.RangeEndAddress);
        Assert.Equal(prefixLength, after.SubnetPrefixLength);
    }

    [Fact]
    public async Task Patch_TurningOnAScopeThatOverlapsAnEnabledOne_Returns400()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, string end, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        await CreateAsync(client, Body("IPv4", start, end, prefixLength));
        DhcpScopeDetail disabled = await CreateAsync(
            client,
            Body("IPv4", start, end, prefixLength, isDisabled: true)
        );

        HttpResponseMessage turnOn = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{disabled.Id}",
            new { isDisabled = false }
        );

        Assert.Equal(HttpStatusCode.BadRequest, turnOn.StatusCode);
        Assert.Contains("overlaps scope", await turnOn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Patch_AScopeStoredWithABadRange_CanStillBeRenamedUntilItsRangeIsPatched()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        // The API refuses this range, so the row is written through the repository, as a
        // restore or an older database would hold it.
        Guid id = Guid.NewGuid();
        string name = $"range-rule-stored-{Guid.NewGuid():N}";
        using (IServiceScope serviceScope = factory.Services.CreateScope())
        {
            await serviceScope
                .ServiceProvider.GetRequiredService<IDhcpScopeRepository>()
                .AddAsync(
                    new DhcpScopeEntity
                    {
                        Id = id,
                        Name = name,
                        ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                        RangeStartAddress = $"{baseAddress}.10",
                        RangeEndAddress = $"{baseAddress[..baseAddress.LastIndexOf('.')]}.254.20",
                        SubnetPrefixLength = 24,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        HttpResponseMessage rename = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{id}",
            new { name = name + "-renamed", comments = "to be fixed" }
        );
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        HttpResponseMessage touchRange = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{id}",
            new { rangeStartAddress = $"{baseAddress}.11" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, touchRange.StatusCode);

        HttpResponseMessage fix = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{id}",
            new { rangeEndAddress = $"{baseAddress}.20" }
        );
        Assert.Equal(HttpStatusCode.NoContent, fix.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpLeasesController" /> end to end, against a real,
///     in-process host and a real SQLite database. A lease is a live artifact the allocation
///     engine itself creates, never an operator-created REST resource, so every lease this test
///     reads back is seeded directly through <see cref="ILeaseRepository" /> first -- exactly the
///     shape <c>Dhcpv4LeaseAllocationService</c> itself writes in production (see
///     <c>Ninlil.Dhcp.Server.Core.Tests</c> for that write path's own dedicated coverage) -- so
///     this test only proves the REST surface reads real, already-persisted rows back correctly,
///     including the optional per-scope <c>scopeId</c> narrowing <see cref="DhcpLeasesController" />'s
///     own doc comment documents.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpLeasesControllerTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task List_WithAScopeIdFilter_ReturnsOnlyLeasesInThatScope()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeAName = $"lease-scope-a-{Guid.NewGuid():N}";
        string scopeBName = $"lease-scope-b-{Guid.NewGuid():N}";

        // Create one real scope through the REST surface -- its own identifier is what a caller
        // actually has on hand to filter by.
        HttpResponseMessage scopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = scopeAName,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.60.100",
                rangeEndAddress = "192.168.60.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        scopeResponse.EnsureSuccessStatusCode();
        DhcpScopeDetail scopeA = await scopeResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        // Seed two real, persisted leases directly through the repository -- one in scope A, one
        // in an unrelated scope B this test never creates through REST at all (a lease is keyed
        // by scope name, not identifier, so no real DhcpScopeEntity needs to exist for it).
        Guid leaseInScopeAId = Guid.NewGuid();
        Guid leaseInScopeBId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = leaseInScopeAId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeAName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:10",
                    IpAddress = "192.168.60.101",
                    HostName = "lease-a-host",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );
            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = leaseInScopeBId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeBName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:20",
                    IpAddress = "192.168.61.101",
                    HostName = "lease-b-host",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        // The unfiltered listing includes both.
        HttpResponseMessage allResponse = await client.GetAsync("/api/dhcp-leases?take=500");
        allResponse.EnsureSuccessStatusCode();
        PagedResult<DhcpLeaseDetail> allPage = await allResponse.Content.ReadFromJsonAsync<
            PagedResult<DhcpLeaseDetail>
        >();
        Assert.Contains(allPage.Items, lease => lease.Id == leaseInScopeAId);
        Assert.Contains(allPage.Items, lease => lease.Id == leaseInScopeBId);

        // Filtering by scope A's own identifier returns only its own lease.
        HttpResponseMessage filteredResponse = await client.GetAsync(
            $"/api/dhcp-leases?scopeId={scopeA.Id}"
        );
        filteredResponse.EnsureSuccessStatusCode();
        PagedResult<DhcpLeaseDetail> filteredPage =
            await filteredResponse.Content.ReadFromJsonAsync<PagedResult<DhcpLeaseDetail>>();
        DhcpLeaseDetail onlyLease = Assert.Single(filteredPage.Items);
        Assert.Equal(leaseInScopeAId, onlyLease.Id);
        Assert.Equal(scopeAName, onlyLease.ScopeName);

        // A single-lease lookup by identifier works too.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/dhcp-leases/{leaseInScopeAId}"
        );
        getResponse.EnsureSuccessStatusCode();
        DhcpLeaseDetail fetched = await getResponse.Content.ReadFromJsonAsync<DhcpLeaseDetail>();
        Assert.Equal("lease-a-host", fetched.HostName);

        // Filtering by a scope identifier that does not exist is a 404.
        HttpResponseMessage missingScopeResponse = await client.GetAsync(
            $"/api/dhcp-leases?scopeId={Guid.NewGuid()}"
        );
        Assert.Equal(HttpStatusCode.NotFound, missingScopeResponse.StatusCode);
    }

    [Fact]
    public async Task Get_ForAPrefixDelegationLease_ExposesPrefixLength()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"pd-lease-scope-{Guid.NewGuid():N}";

        // A DHCPv6 Prefix Delegation lease has no MAC-shaped client identifier (a DUID instead)
        // and carries a real PrefixLength -- confirming DhcpLeaseDetail actually surfaces it (a
        // real gap this sub-phase closed: it previously had no column for it at all).
        Guid pdLeaseId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = pdLeaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    ScopeName = scopeName,
                    ClientIdentifier = "00:01:00:01:2c:8b:9c:3d:aa:bb:cc:dd:ee:ff",
                    IpAddress = "2001:db8:1234::",
                    PrefixLength = 60,
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        HttpResponseMessage getResponse = await client.GetAsync($"/api/dhcp-leases/{pdLeaseId}");
        getResponse.EnsureSuccessStatusCode();
        DhcpLeaseDetail fetched = await getResponse.Content.ReadFromJsonAsync<DhcpLeaseDetail>();
        Assert.Equal(60, fetched.PrefixLength);
        Assert.Equal(InternetProtocolVersionEnum.IPv6, fetched.ProtocolVersion);
    }

    [Fact]
    public async Task DhcpLeasesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-leases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

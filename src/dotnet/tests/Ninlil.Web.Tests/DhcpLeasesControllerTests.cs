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
///     This class covers <see cref="DhcpLeasesController" /> over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator. Leases are created by the allocation
///     engine, not through the API, so the tests save them directly through
///     <see cref="ILeaseRepository" /> in the shape <c>Dhcpv4LeaseAllocationService</c> writes
///     (that write path is covered in <c>Ninlil.Dhcp.Server.Core.Tests</c>) and then check what
///     the endpoint returns, including the optional <c>scopeId</c> filter and the prefix length of
///     a prefix delegation lease.
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

        // Create a scope through the API, since its identifier is what a caller filters by.
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

        // Save two leases through the repository: one in scope A and one in scope B, which is
        // never created. A lease refers to its scope by name, so no scope row is needed for it.
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

        // Filtering by scope A's identifier returns only its lease.
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

        // A DHCPv6 prefix delegation lease has a DUID as its client identifier and carries a
        // PrefixLength, which DhcpLeaseDetail must return.
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

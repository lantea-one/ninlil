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
///     This class covers <see cref="DhcpScopesController.Statistics" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. Lease and reservation rows are
///     saved directly through the repositories, in the shape the allocation engine in
///     <c>Ninlil.Dhcp.Server.Core</c> writes, and the tests check that the reported figures match
///     those rows and change when they change. It also covers the scope integrity rules of
///     <see cref="DhcpScopesController.Replace" /> and <see cref="DhcpScopesController.Patch" />:
///     a rename keeps the scope's leases, and bad durations, a reversed range or a protocol
///     change are refused with 400 and leave the scope as it was.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpScopesControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task Statistics_ForARealPopulatedScope_ReportsGenuineComputedFigures()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"stats-scope-{Guid.NewGuid():N}";

        // A 10-address pool (192.168.90.1 to 192.168.90.10), small enough to count by hand.
        HttpResponseMessage scopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = scopeName,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.90.1",
                rangeEndAddress = "192.168.90.10",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        scopeResponse.EnsureSuccessStatusCode();
        DhcpScopeDetail scope = await scopeResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        // No leases or reservations yet, so all 10 addresses are available.
        DhcpScopeStatisticsDetail empty = await GetStatisticsAsync(client, scope.Id);
        Assert.Equal("10", empty.TotalSlots);
        Assert.Equal(0, empty.LeasedCount);
        Assert.Equal(0, empty.OfferedCount);
        Assert.Equal(0, empty.ReservedCount);
        Assert.Equal("10", empty.AvailableSlots);

        Guid activeLeaseId = Guid.NewGuid();
        Guid offeredLeaseId = Guid.NewGuid();
        Guid disabledReservationId = Guid.NewGuid();
        Guid enabledReservationId = Guid.NewGuid();

        using (IServiceScope scope1 = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope1.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IDhcpReservationRepository reservationRepository =
                scope1.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
            IUnitOfWork unitOfWork = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // One currently active lease.
            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = activeLeaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:01",
                    IpAddress = "192.168.90.2",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );

            // One offered, not yet confirmed lease, which still takes a slot.
            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = offeredLeaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:02",
                    IpAddress = "192.168.90.3",
                    State = LeaseStateEnum.Offered,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
                }
            );

            // One enabled reservation, on an address with no lease.
            await reservationRepository.AddAsync(
                new DhcpReservationEntity
                {
                    Id = enabledReservationId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scope.Id,
                    ClientIdentifier = "aa:bb:cc:dd:ee:03",
                    IpAddress = "192.168.90.4",
                    IsDisabled = false,
                }
            );

            // One disabled reservation, which must not count against ReservedCount or
            // AvailableSlots, since a disabled reservation returns its address to the pool.
            await reservationRepository.AddAsync(
                new DhcpReservationEntity
                {
                    Id = disabledReservationId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scope.Id,
                    ClientIdentifier = "aa:bb:cc:dd:ee:04",
                    IpAddress = "192.168.90.5",
                    IsDisabled = true,
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        DhcpScopeStatisticsDetail populated = await GetStatisticsAsync(client, scope.Id);
        Assert.Equal("10", populated.TotalSlots);
        Assert.Equal(1, populated.LeasedCount);
        Assert.Equal(1, populated.OfferedCount);
        Assert.Equal(1, populated.ReservedCount);
        // 10 total minus 3 occupied (the active lease, the offered lease and the enabled
        // reservation); the disabled reservation does not count.
        Assert.Equal("7", populated.AvailableSlots);

        // Setting the active lease to Released in storage frees its slot.
        using (IServiceScope scope1 = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope1.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();

            LeaseEntity lease = await leaseRepository.GetByIdAsync(activeLeaseId);
            lease.State = LeaseStateEnum.Released;
            leaseRepository.Update(lease);
            await unitOfWork.SaveChangesAsync();
        }

        DhcpScopeStatisticsDetail afterRelease = await GetStatisticsAsync(client, scope.Id);
        Assert.Equal(0, afterRelease.LeasedCount);
        Assert.Equal(1, afterRelease.OfferedCount);
        Assert.Equal(1, afterRelease.ReservedCount);
        Assert.Equal("8", afterRelease.AvailableSlots);
    }

    [Fact]
    public async Task Statistics_WhenAReservedAddressIsAlsoActivelyLeased_DoesNotDoubleCount()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"stats-dedup-scope-{Guid.NewGuid():N}";

        HttpResponseMessage scopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = scopeName,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.91.1",
                rangeEndAddress = "192.168.91.4",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        scopeResponse.EnsureSuccessStatusCode();
        DhcpScopeDetail scope = await scopeResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        using (IServiceScope scope1 = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope1.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IDhcpReservationRepository reservationRepository =
                scope1.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
            IUnitOfWork unitOfWork = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // The reservation and the lease share an address, as when a client holds its
            // reserved address.
            await reservationRepository.AddAsync(
                new DhcpReservationEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scope.Id,
                    ClientIdentifier = "aa:bb:cc:dd:ee:05",
                    IpAddress = "192.168.91.2",
                    IsDisabled = false,
                }
            );
            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:05",
                    IpAddress = "192.168.91.2",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        DhcpScopeStatisticsDetail statistics = await GetStatisticsAsync(client, scope.Id);
        Assert.Equal("4", statistics.TotalSlots);
        Assert.Equal(1, statistics.LeasedCount);
        Assert.Equal(1, statistics.ReservedCount);
        // Only one slot is occupied, not two.
        Assert.Equal("3", statistics.AvailableSlots);
    }

    [Fact]
    public async Task Statistics_ForANonexistentScope_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/dhcp-scopes/{Guid.NewGuid()}/statistics"
        );

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static object ScopeBody(
        string name,
        string protocolVersion = "IPv4",
        string start = null,
        string end = null,
        string defaultLease = "1.00:00:00"
    )
    {
        // A scope write refuses a range that overlaps another enabled scope and the shared host
        // keeps every scope, so a body without a range takes a fresh one.
        if (start is null)
        {
            (string freshStart, string freshEnd, _) =
                protocolVersion == "IPv4"
                    ? DhcpTestScopeRanges.NextIPv4()
                    : DhcpTestScopeRanges.NextIPv6();
            start = freshStart;
            end = freshEnd;
        }

        return new
        {
            name,
            protocolVersion,
            rangeStartAddress = start,
            rangeEndAddress = end,
            subnetPrefixLength = protocolVersion == "IPv4" ? 24 : 64,
            defaultLeaseDuration = defaultLease,
            maxLeaseDuration = "7.00:00:00",
        };
    }

    private async Task<DhcpScopeDetail> CreateScopeWithTwoLeasesAsync(
        HttpClient client,
        string name
    )
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            ScopeBody(name)
        );
        response.EnsureSuccessStatusCode();
        DhcpScopeDetail scope = await response.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        using IServiceScope serviceScope = factory.Services.CreateScope();
        ILeaseRepository leaseRepository =
            serviceScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        IUnitOfWork unitOfWork = serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        for (int i = 1; i <= 2; i++)
        {
            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = name,
                    ClientIdentifier = $"aa:bb:cc:dd:77:0{i}",
                    IpAddress = $"10.77.0.{i}",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );
        }

        await unitOfWork.SaveChangesAsync();

        return scope;
    }

    private static async Task AssertBothLeasesFollowAsync(
        HttpClient client,
        DhcpScopeDetail scope,
        string newName
    )
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/dhcp-leases?scopeId={scope.Id}"
        );
        response.EnsureSuccessStatusCode();
        PagedResult<DhcpLeaseDetail> page = await response.Content.ReadFromJsonAsync<
            PagedResult<DhcpLeaseDetail>
        >();
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, lease => Assert.Equal(newName, lease.ScopeName));

        DhcpScopeStatisticsDetail statistics = await GetStatisticsAsync(client, scope.Id);
        Assert.Equal(2, statistics.LeasedCount);
    }

    [Fact]
    public async Task Patch_RenamingAScope_KeepsItsLeases()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string oldName = $"rename-patch-{Guid.NewGuid():N}";
        string newName = $"renamed-patch-{Guid.NewGuid():N}";
        DhcpScopeDetail scope = await CreateScopeWithTwoLeasesAsync(client, oldName);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            new { name = newName }
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertBothLeasesFollowAsync(client, scope, newName);
    }

    [Fact]
    public async Task Replace_RenamingAScope_KeepsItsLeases()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string oldName = $"rename-put-{Guid.NewGuid():N}";
        string newName = $"renamed-put-{Guid.NewGuid():N}";
        DhcpScopeDetail scope = await CreateScopeWithTwoLeasesAsync(client, oldName);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            ScopeBody(newName)
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertBothLeasesFollowAsync(client, scope, newName);
    }

    [Theory]
    [InlineData("defaultLeaseDuration", "\"banana\"")]
    [InlineData("defaultLeaseDuration", "42")]
    [InlineData("defaultLeaseDuration", "\"-00:05:00\"")]
    [InlineData("defaultLeaseDuration", "\"00:00:00\"")]
    [InlineData("maxLeaseDuration", "\"banana\"")]
    [InlineData("maxLeaseDuration", "42")]
    [InlineData("maxLeaseDuration", "\"-00:05:00\"")]
    [InlineData("maxLeaseDuration", "\"00:00:00\"")]
    public async Task Patch_WithABadDuration_Returns400AndLeavesTheScopeAlone(
        string field,
        string json
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"bad-duration-{Guid.NewGuid():N}";
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            ScopeBody(name)
        );
        createResponse.EnsureSuccessStatusCode();
        DhcpScopeDetail scope = await createResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage response = await client.PatchAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            new StringContent(
                $"{{\"{field}\":{json}}}",
                System.Text.Encoding.UTF8,
                "application/merge-patch+json"
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
        DhcpScopeDetail after = await client.GetFromJsonAsync<DhcpScopeDetail>(
            $"/api/dhcp-scopes/{scope.Id}"
        );
        Assert.Equal(scope.DefaultLeaseDuration, after.DefaultLeaseDuration);
        Assert.Equal(scope.MaxLeaseDuration, after.MaxLeaseDuration);
    }

    [Theory]
    [InlineData("IPv4", "10.78.0.20", "10.78.0.10")]
    [InlineData("IPv6", "fd00:78::20", "fd00:78::10")]
    public async Task Create_Replace_AndPatch_WithStartAfterEnd_Return400(
        string version,
        string start,
        string end
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"range-{Guid.NewGuid():N}";

        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            ScopeBody(name, version, start, end)
        );
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        // Start equal to end is a one-address range and is accepted.
        HttpResponseMessage createOne = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            ScopeBody(name, version, end, end)
        );
        Assert.Equal(HttpStatusCode.Created, createOne.StatusCode);
        DhcpScopeDetail scope = await createOne.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            ScopeBody(name, version, start, end)
        );
        Assert.Equal(HttpStatusCode.BadRequest, replace.StatusCode);

        HttpResponseMessage patch = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            new { rangeStartAddress = start }
        );
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);

        HttpResponseMessage patchOk = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            new { rangeEndAddress = start }
        );
        Assert.Equal(HttpStatusCode.NoContent, patchOk.StatusCode);
    }

    [Fact]
    public async Task Replace_ChangingTheProtocolVersion_Returns400()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"protocol-{Guid.NewGuid():N}";
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            ScopeBody(name)
        );
        createResponse.EnsureSuccessStatusCode();
        DhcpScopeDetail scope = await createResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            ScopeBody(name, "IPv6", "fd00:79::1", "fd00:79::9")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        DhcpScopeDetail after = await client.GetFromJsonAsync<DhcpScopeDetail>(
            $"/api/dhcp-scopes/{scope.Id}"
        );
        Assert.Equal(InternetProtocolVersionEnum.IPv4, after.ProtocolVersion);
    }

    private static async Task<DhcpScopeStatisticsDetail> GetStatisticsAsync(
        HttpClient client,
        Guid scopeId
    )
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/dhcp-scopes/{scopeId}/statistics"
        );
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DhcpScopeStatisticsDetail>();
    }
}

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
///     This class covers <see cref="DhcpScopesController.Statistics" /> end to end, against a real,
///     in-process host and a real SQLite database -- confirming the reported figures genuinely
///     reflect real, currently-recorded lease/reservation rows (seeded directly through the real
///     repositories, mirroring how <c>Ninlil.Dhcp.Server.Core</c>'s own allocation engine writes
///     them in production) rather than a cached or approximate figure, and that the figures move
///     when the underlying data does.
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

        // A /29-shaped, 10-address pool (192.168.90.1-192.168.90.10) -- small enough to reason
        // about by hand.
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

        // No leases/reservations yet -- every one of the 10 addresses is available.
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

            // One currently offered (not yet confirmed) lease -- still occupies a slot.
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

            // One enabled reservation, on an address with no lease of its own at all.
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

            // One disabled reservation -- must not count against ReservedCount or AvailableSlots
            // at all, since a disabled reservation returns its address to the ordinary pool.
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
        // 10 total, minus 3 distinct occupied slots (the active lease, the offered lease, and
        // the one enabled reservation) -- the disabled reservation contributes nothing.
        Assert.Equal("7", populated.AvailableSlots);

        // Releasing the active lease frees its own slot back up.
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

            // Reservation and lease share the exact same address -- the common case of a client
            // that holds its own reserved address.
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
        // Only one distinct slot is actually occupied, not two.
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

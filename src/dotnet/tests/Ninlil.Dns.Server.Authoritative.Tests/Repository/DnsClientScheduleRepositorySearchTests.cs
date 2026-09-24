using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientScheduleRepository" />'s
///     real, pushed-down <c>SearchByClientIdAsync</c>/<c>CountByClientIdAsync</c> pagination
///     against a real SQLite-backed database, mirroring <see cref="ClientGroupRepositorySearchTests" />'s
///     own precedent for the same shape.
/// </summary>
public sealed class DnsClientScheduleRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    private async Task<Guid> SeedClientAsync(IServiceProvider services)
    {
        IDnsClientRepository clientRepository = services.GetRequiredService<IDnsClientRepository>();

        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await clientRepository.AddAsync(client);

        return client.Id;
    }

    private async Task SeedSchedulesAsync(int count)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientScheduleRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Seed the owning client first, in the exact same scope/unit of work, so both rows
        // commit together.
        Guid ownerClientId = await SeedClientAsync(scope.ServiceProvider);

        for (int i = 0; i < count; i++)
        {
            await repository.AddAsync(
                new DnsClientScheduleEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = ownerClientId,
                    DaysOfWeek = DnsScheduleDayOfWeekEnum.Monday,
                    StartTime = new TimeOnly(21, 0),
                    EndTime = new TimeOnly(7, 0),
                    TimeZoneId = "UTC",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(i),
                }
            );
        }

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchByClientIdAsync_Paginated_ReturnsTheCorrectSliceInCreationOrder()
    {
        await SeedSchedulesAsync(5);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository clientRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientScheduleRepository>();

        IReadOnlyList<DnsClientEntity> clients = await clientRepository.SearchAsync(null, 0, 10);
        Guid clientId = Assert.Single(clients).Id;

        IReadOnlyList<DnsClientScheduleEntity> firstPage = await repository.SearchByClientIdAsync(
            clientId,
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsClientScheduleEntity> secondPage = await repository.SearchByClientIdAsync(
            clientId,
            skip: 2,
            take: 2
        );

        Assert.Equal(2, firstPage.Count);
        Assert.Equal(2, secondPage.Count);
        Assert.DoesNotContain(
            secondPage,
            schedule => firstPage.Any(first => first.Id == schedule.Id)
        );

        // Ordered strictly by creation time, ascending.
        Assert.True(firstPage[0].CreatedAtUtc < firstPage[1].CreatedAtUtc);
        Assert.True(firstPage[1].CreatedAtUtc < secondPage[0].CreatedAtUtc);
    }

    [Fact]
    public async Task CountByClientIdAsync_ReflectsTheFullScheduleCountRegardlessOfPageSize()
    {
        await SeedSchedulesAsync(3);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository clientRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientScheduleRepository>();

        IReadOnlyList<DnsClientEntity> clients = await clientRepository.SearchAsync(null, 0, 10);
        Guid clientId = Assert.Single(clients).Id;

        int count = await repository.CountByClientIdAsync(clientId);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ListByClientIdAsync_ReturnsEveryScheduleForTheClientOnly()
    {
        await using AsyncServiceScope seedScope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository clientRepository =
            seedScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IUnitOfWork seedUnitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DnsClientEntity clientA = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        DnsClientEntity clientB = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await clientRepository.AddAsync(clientA);
        await clientRepository.AddAsync(clientB);

        IDnsClientScheduleRepository scheduleRepository =
            seedScope.ServiceProvider.GetRequiredService<IDnsClientScheduleRepository>();
        await scheduleRepository.AddAsync(
            new DnsClientScheduleEntity
            {
                Id = Guid.NewGuid(),
                ClientId = clientA.Id,
                DaysOfWeek = DnsScheduleDayOfWeekEnum.Monday,
                StartTime = new TimeOnly(21, 0),
                EndTime = new TimeOnly(7, 0),
                TimeZoneId = "UTC",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await scheduleRepository.AddAsync(
            new DnsClientScheduleEntity
            {
                Id = Guid.NewGuid(),
                ClientId = clientB.Id,
                DaysOfWeek = DnsScheduleDayOfWeekEnum.Tuesday,
                StartTime = new TimeOnly(21, 0),
                EndTime = new TimeOnly(7, 0),
                TimeZoneId = "UTC",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await seedUnitOfWork.SaveChangesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientScheduleRepository>();

        IReadOnlyList<DnsClientScheduleEntity> schedulesForClientA =
            await repository.ListByClientIdAsync(clientA.Id);

        DnsClientScheduleEntity onlySchedule = Assert.Single(schedulesForClientA);
        Assert.Equal(clientA.Id, onlySchedule.ClientId);
    }
}

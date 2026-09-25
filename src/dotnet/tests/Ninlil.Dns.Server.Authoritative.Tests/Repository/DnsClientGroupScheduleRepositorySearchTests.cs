using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientGroupScheduleRepository" /> (<c>SearchByClientGroupIdAsync</c>, <c>CountByClientGroupIdAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as the client group schedule endpoint's listing needs, rather
///     than in memory.
/// </summary>
public sealed class DnsClientGroupScheduleRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    private async Task<Guid> SeedGroupAsync(IServiceProvider services)
    {
        IDnsClientGroupRepository groupRepository =
            services.GetRequiredService<IDnsClientGroupRepository>();

        DnsClientGroupEntity group = new()
        {
            Id = Guid.NewGuid(),
            Name = "kids-devices",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await groupRepository.AddAsync(group);

        return group.Id;
    }

    private async Task<Guid> SeedSchedulesAsync(int count)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupScheduleRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid groupId = await SeedGroupAsync(scope.ServiceProvider);

        for (int i = 0; i < count; i++)
        {
            await repository.AddAsync(
                new DnsClientGroupScheduleEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    DaysOfWeek = DnsScheduleDayOfWeekEnum.All,
                    StartTime = new TimeOnly(22, 0),
                    EndTime = new TimeOnly(6, 30),
                    TimeZoneId = "UTC",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(i),
                }
            );
        }

        await unitOfWork.SaveChangesAsync();

        return groupId;
    }

    [Fact]
    public async Task SearchByClientGroupIdAsync_Paginated_ReturnsTheCorrectSliceInCreationOrder()
    {
        Guid groupId = await SeedSchedulesAsync(5);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupScheduleRepository>();

        IReadOnlyList<DnsClientGroupScheduleEntity> firstPage =
            await repository.SearchByClientGroupIdAsync(groupId, skip: 0, take: 2);
        IReadOnlyList<DnsClientGroupScheduleEntity> secondPage =
            await repository.SearchByClientGroupIdAsync(groupId, skip: 2, take: 2);

        Assert.Equal(2, firstPage.Count);
        Assert.Equal(2, secondPage.Count);
        Assert.DoesNotContain(
            secondPage,
            schedule => firstPage.Any(first => first.Id == schedule.Id)
        );
        Assert.True(firstPage[0].CreatedAtUtc < firstPage[1].CreatedAtUtc);
        Assert.True(firstPage[1].CreatedAtUtc < secondPage[0].CreatedAtUtc);
    }

    [Fact]
    public async Task CountByClientGroupIdAsync_ReflectsTheFullScheduleCountRegardlessOfPageSize()
    {
        Guid groupId = await SeedSchedulesAsync(3);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupScheduleRepository>();

        int count = await repository.CountByClientGroupIdAsync(groupId);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ListByClientGroupIdAsync_ReturnsEveryScheduleForTheGroupOnly()
    {
        await using AsyncServiceScope seedScope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupRepository groupRepository =
            seedScope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();
        IUnitOfWork seedUnitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DnsClientGroupEntity groupA = new()
        {
            Id = Guid.NewGuid(),
            Name = "group-a",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        DnsClientGroupEntity groupB = new()
        {
            Id = Guid.NewGuid(),
            Name = "group-b",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await groupRepository.AddAsync(groupA);
        await groupRepository.AddAsync(groupB);

        IDnsClientGroupScheduleRepository scheduleRepository =
            seedScope.ServiceProvider.GetRequiredService<IDnsClientGroupScheduleRepository>();
        await scheduleRepository.AddAsync(
            new DnsClientGroupScheduleEntity
            {
                Id = Guid.NewGuid(),
                ClientGroupId = groupA.Id,
                DaysOfWeek = DnsScheduleDayOfWeekEnum.Monday,
                StartTime = new TimeOnly(21, 0),
                EndTime = new TimeOnly(7, 0),
                TimeZoneId = "UTC",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await scheduleRepository.AddAsync(
            new DnsClientGroupScheduleEntity
            {
                Id = Guid.NewGuid(),
                ClientGroupId = groupB.Id,
                DaysOfWeek = DnsScheduleDayOfWeekEnum.Tuesday,
                StartTime = new TimeOnly(21, 0),
                EndTime = new TimeOnly(7, 0),
                TimeZoneId = "UTC",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await seedUnitOfWork.SaveChangesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupScheduleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupScheduleRepository>();

        IReadOnlyList<DnsClientGroupScheduleEntity> schedulesForGroupA =
            await repository.ListByClientGroupIdAsync(groupA.Id);

        DnsClientGroupScheduleEntity onlySchedule = Assert.Single(schedulesForGroupA);
        Assert.Equal(groupA.Id, onlySchedule.ClientGroupId);
    }
}

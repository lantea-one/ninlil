using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientGroupListAssignmentRepository" /> (<c>SearchByGroupIdAsync</c>, <c>CountByGroupIdAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>ClientGroupListAssignmentsController</c>'s listing needs, rather
///     than in memory. It also checks that another group's
///     assignments never appear. The entity is ordered by <see cref="EntityBase.CreatedAtUtc" />,
///     which SQLite can only sort because <c>DnsClientGroupListAssignmentConfiguration</c> stores it as
///     UTC ticks; without that conversion these tests throw <see cref="NotSupportedException" />.
/// </summary>
public sealed class ClientGroupListAssignmentRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one target group's five list assignments, out of
    ///     creation-time order, plus one extra assignment on a different group, so a
    ///     passing group-scoping assertion can only be explained by a translated
    ///     <c>WHERE ClientGroupId = ...</c> clause, not by the target group simply happening to be
    ///     the only group with any assignments at all.
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupListAssignmentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupListAssignmentRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid targetGroupId = Guid.NewGuid();
        Guid otherGroupId = Guid.NewGuid();
        DateTimeOffset baseTime = DateTimeOffset.UtcNow;

        int[] offsetMinutesInSeedOrder = [40, 10, 30, 0, 20];

        foreach (int offsetMinutes in offsetMinutesInSeedOrder)
            await repository.AddAsync(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = baseTime.AddMinutes(offsetMinutes),
                    ClientGroupId = targetGroupId,
                    ListId = Guid.NewGuid(),
                }
            );

        await repository.AddAsync(
            new DnsClientGroupListAssignmentEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = baseTime,
                ClientGroupId = otherGroupId,
                ListId = Guid.NewGuid(),
            }
        );

        await unitOfWork.SaveChangesAsync();

        return targetGroupId;
    }

    [Fact]
    public async Task SearchByGroupIdAsync_ReturnsOnlyTheTargetGroupsAssignments_OrderedByCreatedAt()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupListAssignmentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupListAssignmentRepository>();

        IReadOnlyList<DnsClientGroupListAssignmentEntity> results =
            await repository.SearchByGroupIdAsync(targetGroupId, skip: 0, take: 50);

        Assert.Equal(5, results.Count);
        Assert.All(results, assignment => Assert.Equal(targetGroupId, assignment.ClientGroupId));

        // Ordered by creation time ascending -- the offsets seeded out of order above (40, 10,
        // 30, 0, 20 minutes past a shared base time) must sort back to 0, 10, 20, 30, 40, a
        // result only explainable by a translated ORDER BY over the converted UTC-ticks
        // column, not by seed/insertion order already matching.
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].CreatedAtUtc < results[i].CreatedAtUtc);
    }

    [Fact]
    public async Task SearchByGroupIdAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupListAssignmentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupListAssignmentRepository>();

        IReadOnlyList<DnsClientGroupListAssignmentEntity> firstPage =
            await repository.SearchByGroupIdAsync(targetGroupId, skip: 0, take: 2);
        IReadOnlyList<DnsClientGroupListAssignmentEntity> thirdPage =
            await repository.SearchByGroupIdAsync(targetGroupId, skip: 4, take: 2);

        Assert.Equal(2, firstPage.Count);
        Assert.Single(thirdPage);
        Assert.DoesNotContain(
            thirdPage,
            assignment => firstPage.Any(first => first.Id == assignment.Id)
        );
    }

    [Fact]
    public async Task CountByGroupIdAsync_ReflectsOnlyTheTargetGroupsAssignmentCount()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupListAssignmentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupListAssignmentRepository>();

        int count = await repository.CountByGroupIdAsync(targetGroupId);

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task SearchByGroupIdAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        Guid targetGroupId = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientGroupListAssignmentRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupListAssignmentRepository>();

        IReadOnlyList<DnsClientGroupListAssignmentEntity> results =
            await repository.SearchByGroupIdAsync(targetGroupId, skip: 100, take: 50);

        Assert.Empty(results);
    }
}

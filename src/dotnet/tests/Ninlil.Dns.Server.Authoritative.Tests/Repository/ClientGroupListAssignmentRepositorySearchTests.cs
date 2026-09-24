using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientGroupListAssignmentRepository" />'s
///     real, pushed-down <c>SearchByGroupIdAsync</c>/<c>CountByGroupIdAsync</c> pagination against
///     a real SQLite-backed database -- the Phase 13j proof that
///     <c>ClientGroupListAssignmentsController</c>'s own assignment listing no longer materializes
///     every assignment into memory before slicing a page out of it, and that a second group's own
///     assignments never leak into another group's page, mirroring
///     <see cref="DnssecKeyRepositorySearchTests" />'s own Phase 13h proof for the same shape. This
///     entity orders by <see cref="EntityBase.CreatedAtUtc" /> -- a raw <see cref="DateTimeOffset" />
///     column SQLite's own EF Core provider has never supported translating an <c>ORDER BY</c>
///     over, fixed for this entity the same way <c>DnssecKeyConfiguration</c>/
///     <c>DnsRewriteConfiguration</c> already fixed it for their own <c>CreatedAtUtc</c> columns
///     (see <c>DnsClientGroupListAssignmentConfiguration</c>'s own <c>HasConversion</c> to UTC
///     ticks) -- without that conversion, every test below would fail with a real
///     <see cref="NotSupportedException" />, not merely an assertion mismatch.
/// </summary>
public sealed class ClientGroupListAssignmentRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one target group's five list assignments, deliberately out of
    ///     creation-time order, plus one extra assignment on an entirely different group, so a
    ///     passing group-scoping assertion can only be explained by a real, translated
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
        // result only explainable by a real, translated ORDER BY over the converted UTC-ticks
        // column, not by seed/insertion order happening to already match.
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

using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Tests;

/// <summary>
///     This class tests <see cref="PresetFilterListApplier" /> with the in-memory repositories in
///     <c>InMemoryRepositories.cs</c>: default-group creation and reuse, list and assignment
///     creation, and that applying a preset again updates instead of duplicating.
/// </summary>
public class PresetFilterListApplierTests
{
    private sealed class Fixture
    {
        public readonly InMemoryDnsClientGroupRepository ClientGroups = new();
        public readonly InMemoryDnsFilterListRepository FilterLists = new();
        public readonly InMemoryDnsClientGroupListAssignmentRepository Assignments = new();
        public readonly InMemoryUnitOfWork UnitOfWork = new();
        public readonly PresetFilterListApplier Applier;

        public Fixture() =>
            Applier = new PresetFilterListApplier(
                ClientGroups,
                FilterLists,
                Assignments,
                UnitOfWork
            );
    }

    [Fact]
    public async Task ApplyingAPresetWithNoSubscriptionsDoesNothingAtAll()
    {
        Fixture fixture = new();

        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Baseline);

        Assert.Empty(fixture.ClientGroups.Items);
        Assert.Empty(fixture.FilterLists.Items);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ApplyingProtectiveCreatesADefaultGroupOneListAndOneAssignment()
    {
        Fixture fixture = new();

        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Protective);

        DnsClientGroupEntity defaultGroup = Assert.Single(fixture.ClientGroups.Items);
        Assert.True(defaultGroup.IsDefault);

        DnsFilterListEntity list = Assert.Single(fixture.FilterLists.Items);
        Assert.Equal("Ninlil Preset: Protective Threat Feed", list.Name);

        DnsClientGroupListAssignmentEntity assignment = Assert.Single(fixture.Assignments.Items);
        Assert.Equal(defaultGroup.Id, assignment.ClientGroupId);
        Assert.Equal(list.Id, assignment.ListId);

        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ApplyingFamilyCreatesBothListsAssignedToTheSameDefaultGroup()
    {
        Fixture fixture = new();

        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Family);

        Assert.Single(fixture.ClientGroups.Items);
        Assert.Equal(2, fixture.FilterLists.Items.Count);
        Assert.Equal(2, fixture.Assignments.Items.Count);
        Assert.All(
            fixture.Assignments.Items,
            assignment => Assert.Equal(fixture.ClientGroups.Items[0].Id, assignment.ClientGroupId)
        );
    }

    [Fact]
    public async Task ApplyingTheSamePresetTwiceDoesNotDuplicateAnything()
    {
        Fixture fixture = new();

        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Protective);
        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Protective);

        Assert.Single(fixture.ClientGroups.Items);
        Assert.Single(fixture.FilterLists.Items);
        Assert.Single(fixture.Assignments.Items);
    }

    [Fact]
    public async Task ApplyingProtectiveReusesAnAlreadyExistingDefaultGroup()
    {
        Fixture fixture = new();
        DnsClientGroupEntity existingDefault = new()
        {
            Id = Guid.NewGuid(),
            Name = "Already Here",
            IsDefault = true,
        };
        fixture.ClientGroups.Items.Add(existingDefault);

        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Protective);

        Assert.Single(fixture.ClientGroups.Items);

        DnsClientGroupListAssignmentEntity assignment = Assert.Single(fixture.Assignments.Items);
        Assert.Equal(existingDefault.Id, assignment.ClientGroupId);
    }

    [Fact]
    public async Task ReapplyingProtectiveAfterItsSubscriptionUrlWouldChangeUpdatesInPlace()
    {
        Fixture fixture = new();
        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Protective);

        DnsFilterListEntity original = fixture.FilterLists.Items[0];
        original.SubscriptionUrl = "https://stale.example/old-list.txt";

        await fixture.Applier.ApplyAsync(DnsServerPresetCatalog.Protective);

        DnsFilterListEntity updated = Assert.Single(fixture.FilterLists.Items);
        Assert.Equal(
            DnsServerPresetCatalog.Protective.FilterListSubscriptions[0].SubscriptionUrl,
            updated.SubscriptionUrl
        );
    }
}

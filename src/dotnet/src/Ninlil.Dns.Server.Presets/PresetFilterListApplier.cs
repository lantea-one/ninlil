using System.Linq;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class applies the <see cref="PresetDefinition.FilterListSubscriptions" /> of a
///     preset. It makes sure a default client group exists, creates or updates one
///     <c>DnsFilterListEntity</c> per subscription (matched by its preset-owned name, so applying
///     the same preset again updates the list instead of duplicating it), and assigns each list to
///     the default group. It only writes the subscription records; <c>DnsFilterListExternalRefreshService</c>
///     fetches and parses the lists on its schedule.
/// </summary>
/// <param name="clientGroupRepository">The repository used to find or create the default client group.</param>
/// <param name="filterListRepository">The repository used to find or create each subscription's list.</param>
/// <param name="assignmentRepository">The repository used to find or create each list's assignment to the default group.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through, once, at the end.</param>
public sealed class PresetFilterListApplier(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository assignmentRepository,
    IUnitOfWork unitOfWork
)
{
    /// <summary>
    ///     This method applies every subscription of <paramref name="preset" /> and commits all
    ///     changes with one <see cref="IUnitOfWork.SaveChangesAsync" /> call at the end. It does
    ///     nothing, and creates no default group, when the preset has no subscriptions, which is
    ///     the case for every built-in preset except <c>Protective</c> and <c>Family</c>.
    /// </summary>
    /// <param name="preset">The preset whose filter-list subscriptions should be applied.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ApplyAsync(PresetDefinition preset, CancellationToken stoppingToken = default)
    {
        if (preset.FilterListSubscriptions.Count == 0)
            return;

        DnsClientGroupEntity defaultGroup = await EnsureDefaultClientGroupAsync(stoppingToken);

        foreach (PresetFilterListSubscription subscription in preset.FilterListSubscriptions)
        {
            DnsFilterListEntity list = await UpsertFilterListAsync(subscription, stoppingToken);
            await EnsureAssignmentAsync(defaultGroup.Id, list.Id, stoppingToken);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method finds the default client group, or creates one if none exists.
    ///     <c>DnsFilterEngine</c> uses a default group when there is one but never creates it, so a
    ///     preset that attaches lists to the default group has to make sure it exists.
    /// </summary>
    private async Task<DnsClientGroupEntity> EnsureDefaultClientGroupAsync(
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupEntity defaultGroup = await clientGroupRepository.GetDefaultAsync(
            stoppingToken
        );

        if (defaultGroup is not null)
            return defaultGroup;

        defaultGroup = new DnsClientGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            IsDefault = true,
            IsDisabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await clientGroupRepository.AddAsync(defaultGroup, stoppingToken);
        return defaultGroup;
    }

    /// <summary>
    ///     This method finds a subscription's list by its preset-owned name and updates it in
    ///     place (so a changed subscription URL is picked up when the preset is applied again), or
    ///     creates it if no list has that name. An existing list is also re-enabled.
    /// </summary>
    private async Task<DnsFilterListEntity> UpsertFilterListAsync(
        PresetFilterListSubscription subscription,
        CancellationToken stoppingToken
    )
    {
        DnsFilterListEntity existing = await filterListRepository.FindByNameAsync(
            subscription.Name,
            stoppingToken
        );

        if (existing is not null)
        {
            existing.Kind = subscription.Kind;
            existing.Action = subscription.Action;
            existing.Source = DnsFilterListSourceEnum.Subscribed;
            existing.SubscriptionUrl = subscription.SubscriptionUrl;
            existing.RefreshInterval = subscription.RefreshInterval;
            existing.IsDisabled = false;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            filterListRepository.Update(existing);
            return existing;
        }

        DnsFilterListEntity created = new()
        {
            Id = Guid.NewGuid(),
            Name = subscription.Name,
            Kind = subscription.Kind,
            Action = subscription.Action,
            Source = DnsFilterListSourceEnum.Subscribed,
            SubscriptionUrl = subscription.SubscriptionUrl,
            RefreshInterval = subscription.RefreshInterval,
            IsDisabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await filterListRepository.AddAsync(created, stoppingToken);
        return created;
    }

    /// <summary>
    ///     This method ensures the default client group is assigned to a list, creating the
    ///     assignment only if it does not already exist.
    /// </summary>
    private async Task EnsureAssignmentAsync(
        Guid clientGroupId,
        Guid listId,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<DnsClientGroupListAssignmentEntity> existingAssignments =
            await assignmentRepository.ListByGroupIdAsync(clientGroupId, stoppingToken);

        if (existingAssignments.Any(assignment => assignment.ListId == listId))
            return;

        await assignmentRepository.AddAsync(
            new DnsClientGroupListAssignmentEntity
            {
                Id = Guid.NewGuid(),
                ClientGroupId = clientGroupId,
                ListId = listId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            },
            stoppingToken
        );
    }
}

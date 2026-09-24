using System.Linq;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class applies a <see cref="PresetDefinition" />'s own <see cref="PresetDefinition.FilterListSubscriptions" />:
///     ensuring a default client group exists, upserting each subscription's own
///     <c>DnsFilterListEntity</c> (matched by its own stable, preset-owned name -- so re-applying
///     the same preset updates rather than duplicates it), and ensuring an assignment from the
///     default group to that list exists. Actually fetching and parsing each subscribed URL's
///     own content remains <c>DnsFilterListExternalRefreshService</c>'s job, on its own schedule
///     -- this class only ever creates the subscription record, never fetches anything itself.
/// </summary>
/// <param name="clientGroupRepository">The repository used to find or create the default client group.</param>
/// <param name="filterListRepository">The repository used to find or create each subscription's own list.</param>
/// <param name="assignmentRepository">The repository used to find or create each list's own assignment to the default group.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through, once, at the end.</param>
public sealed class PresetFilterListApplier(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository assignmentRepository,
    IUnitOfWork unitOfWork
)
{
    /// <summary>
    ///     This method applies every one of <paramref name="preset" />'s own subscriptions,
    ///     committing all staged changes in a single <see cref="IUnitOfWork.SaveChangesAsync" />
    ///     call once every subscription has been processed. A no-op (no default group is
    ///     created, nothing is committed) if <paramref name="preset" /> has no subscriptions at
    ///     all -- true for every built-in preset except <c>Protective</c> and <c>Family</c>.
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
    ///     This method finds this server's own default client group, or creates one if none
    ///     exists yet -- <c>DnsFilterEngine</c>'s own client-group resolution assumes a default
    ///     group might exist but never creates one itself, so a preset that needs to attach a
    ///     list to "the default group" must be able to guarantee one is actually there.
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
    ///     This method finds a subscription's own previously-created list by its stable,
    ///     preset-owned name, updating it in place if found (so re-applying a preset whose
    ///     subscription URL has since changed picks up the new URL), or creates it if this is
    ///     the first time this preset has ever been applied.
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

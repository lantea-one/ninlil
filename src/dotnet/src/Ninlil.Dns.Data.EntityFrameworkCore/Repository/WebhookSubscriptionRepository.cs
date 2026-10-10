using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IWebhookSubscriptionRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class WebhookSubscriptionRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<WebhookSubscriptionEntity>(context),
        IWebhookSubscriptionRepository
{
    /// <inheritdoc />
    public Task<WebhookSubscriptionEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(subscription => subscription.Name == name, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookSubscriptionEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(subscription => !subscription.IsDisabled)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookSubscriptionEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .OrderBy(subscription => subscription.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}

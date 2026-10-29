using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IWebhookDeliveryAttemptRepository" /> against
///     <see cref="NinlilDnsContext" />. The retention sweep uses <c>ExecuteDeleteAsync</c>, a
///     single <c>DELETE ... WHERE ...</c> without loading rows, as
///     <see cref="DnsQueryLogRepository.DeleteOlderThanAsync" /> does.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class WebhookDeliveryAttemptRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<WebhookDeliveryAttemptEntity>(context),
        IWebhookDeliveryAttemptRepository
{
    /// <inheritdoc />
    /// <remarks>
    ///     The filter and the first sort key are served by the <c>(Status, NextRetryDueUtc)</c>
    ///     index. The id breaks ties so that the same rows come back for the same data. The rows
    ///     are tracked, because the sweep changes and saves them.
    /// </remarks>
    public async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        int maxCount,
        CancellationToken stoppingToken = default
    )
    {
        if (maxCount < 1)
            return [];

        return await Set.Where(attempt =>
                attempt.Status == WebhookDeliveryAttemptStatusEnum.Pending
                && attempt.NextRetryDueUtc != null
                && attempt.NextRetryDueUtc <= asOfUtc
            )
            .OrderBy(attempt => attempt.NextRetryDueUtc)
            .ThenBy(attempt => attempt.Id)
            .Take(maxCount)
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(attempt =>
                attempt.Status != WebhookDeliveryAttemptStatusEnum.Pending
                && attempt.CompletedAtUtc != null
                && attempt.CompletedAtUtc < cutoffUtc
            )
            .ExecuteDeleteAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> SearchBySubscriptionIdAsync(
        Guid subscriptionId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(attempt => attempt.SubscriptionId == subscriptionId)
            .OrderByDescending(attempt => attempt.CreatedAtUtc)
            .ThenByDescending(attempt => attempt.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(attempt => attempt.SubscriptionId == subscriptionId, stoppingToken);
}

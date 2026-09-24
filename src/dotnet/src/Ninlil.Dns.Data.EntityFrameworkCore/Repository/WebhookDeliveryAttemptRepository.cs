using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IWebhookDeliveryAttemptRepository" /> against
///     <see cref="NinlilDnsContext" />, using Entity Framework Core's own
///     <c>ExecuteDeleteAsync</c> query extension for the retention sweep -- the same
///     "translated <c>DELETE ... WHERE ...</c>, never a materialize-then-discard" reasoning
///     <see cref="DnsQueryLogRepository.DeleteOlderThanAsync" /> already establishes.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class WebhookDeliveryAttemptRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<WebhookDeliveryAttemptEntity>(context),
        IWebhookDeliveryAttemptRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(attempt =>
                attempt.Status == WebhookDeliveryAttemptStatusEnum.Pending
                && attempt.NextRetryDueUtc != null
                && attempt.NextRetryDueUtc <= asOfUtc
            )
            .ToListAsync(stoppingToken);

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
}

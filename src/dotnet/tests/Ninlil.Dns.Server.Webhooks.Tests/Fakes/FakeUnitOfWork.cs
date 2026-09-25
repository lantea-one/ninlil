using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests.Fakes;

/// <summary>
///     This class is an <see cref="IUnitOfWork" /> that saves nothing and counts
///     <see cref="SaveChangesAsync" /> calls. Writes to <see cref="FakeWebhookDeliveryAttemptRepository" />
///     change its in-memory list and entities directly, so there is nothing to commit.
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}

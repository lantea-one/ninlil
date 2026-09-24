using Ninlil.Dns.Data.Abstractions.Repository;

namespace Ninlil.Dns.Server.Webhooks.Tests.Fakes;

/// <summary>
///     This class is a no-op <see cref="IUnitOfWork" /> stand-in for this project's own unit
///     tests: every write below against <see cref="FakeWebhookDeliveryAttemptRepository" /> is
///     already directly visible (it mutates a shared in-memory list/object graph), so there is
///     nothing left for a real "commit" step to actually do.
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

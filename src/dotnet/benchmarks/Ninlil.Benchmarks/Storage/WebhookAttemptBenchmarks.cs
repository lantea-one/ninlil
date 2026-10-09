using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the delivery-attempt page of a webhook subscription the way
///     <c>WebhooksController</c> builds it: list every attempt of the subscription, sort by
///     creation time and slice in memory.
/// </summary>
[Config(typeof(StorageConfig))]
public class WebhookAttemptBenchmarks : StorageBenchmarkBase
{
    private Guid _subscriptionId;

    /// <inheritdoc />
    protected override async Task OnSetupAsync() =>
        _subscriptionId = await InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IWebhookSubscriptionRepository>()
                    .ListAsync(subscription =>
                        subscription.Name == BenchmarkDataSet.WebhookSubscriptionName
                    )
            )[0].Id
        );

    private Task<int> Page(int skip) =>
        InScopeAsync(async provider =>
        {
            IReadOnlyList<WebhookDeliveryAttemptEntity> attempts = await provider
                .GetRequiredService<IWebhookDeliveryAttemptRepository>()
                .ListAsync(attempt => attempt.SubscriptionId == _subscriptionId);

            return attempts
                .OrderByDescending(attempt => attempt.CreatedAtUtc)
                .Skip(skip)
                .Take(50)
                .Count();
        });

    /// <summary>The first page.</summary>
    [Benchmark]
    public Task<int> FirstPage() => Page(0);

    /// <summary>A page four fifths down the list.</summary>
    [Benchmark]
    public Task<int> DeepPage() => Page(Shape.WebhookAttempts * 4 / 5);
}

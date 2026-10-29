using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures what one webhook retry sweep reads when 10,000 deliveries are due, as
///     <c>WebhookDeliveryRetrySweepService.SweepAsync</c> does: all at once
///     (<see cref="ReadAllAtOnce" />, one read of every due row) and in batches of
///     <c>WebhookDeliveryRetrySweepService.RetryBatchSize</c> rows, each batch in its own scope
///     (<see cref="ReadInBatches" />). The resume step is a stand-in that moves each row out of
///     the due state (it marks the row succeeded and saves, as a real resume saves its outcomes)
///     without sending a request, so the time includes one write per row in both variants.
///     The rows belong to one marker subscription; <see cref="TopUp" /> replaces them outside the
///     measurement before every iteration, because a sweep leaves none due. It runs three
///     monitored iterations of one invocation each. Both benchmarks return the number of rows
///     resumed and setup checks that it is 10,000.
/// </summary>
[Config(typeof(MonitoredConfig))]
public class WebhookRetrySweepBenchmarks : StorageBenchmarkBase
{
    private const int DueRows = 10_000;

    private static readonly Guid MarkerSubscriptionId = new("b0b0b0b0-0000-4000-8000-000000000011");

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        TopUp();
        int resumed = await ReadAllAtOnce();
        if (resumed != DueRows)
            throw new InvalidOperationException($"Expected {DueRows} due rows, resumed {resumed}.");

        TopUp();
        resumed = await ReadInBatches();
        if (resumed != DueRows)
            throw new InvalidOperationException($"Expected {DueRows} due rows, resumed {resumed}.");
    }

    /// <inheritdoc />
    protected override Task OnCleanupAsync() => RemoveMarkerRowsAsync();

    /// <summary>
    ///     This method replaces the marker rows with 10,000 due ones, so every iteration starts
    ///     from the same state. The due times are spread over the last ten minutes so that the
    ///     oldest-first order is a real order.
    /// </summary>
    [IterationSetup]
    public void TopUp()
    {
        RemoveMarkerRowsAsync().GetAwaiter().GetResult();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeedWriter writer = new(Database.Services, TextWriter.Null);
        writer
            .AddAllAsync<WebhookDeliveryAttemptEntity, IWebhookDeliveryAttemptRepository>(
                "due attempts",
                Enumerable
                    .Range(0, DueRows)
                    .Select(n => new WebhookDeliveryAttemptEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = now.AddMinutes(-20),
                        SubscriptionId = MarkerSubscriptionId,
                        SubscriptionName = "bench-retry-sweep",
                        EventTypeName = "DnsCacheEvicted",
                        PayloadJson = "{\"evicted\":42}",
                        Status = WebhookDeliveryAttemptStatusEnum.Pending,
                        AttemptCount = 1,
                        MaxAttempts = 5,
                        LastAttemptedAtUtc = now.AddMinutes(-20),
                        LastHttpStatusCode = 500,
                        NextRetryDueUtc = now.AddMinutes(-10).AddMilliseconds(n * 50),
                    })
            )
            .GetAwaiter()
            .GetResult();
    }

    private Task RemoveMarkerRowsAsync() =>
        InScopeAsync(async provider =>
        {
            IWebhookDeliveryAttemptRepository repository =
                provider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
            foreach (
                WebhookDeliveryAttemptEntity row in await repository.ListAsync(attempt =>
                    attempt.SubscriptionId == MarkerSubscriptionId
                )
            )
                repository.Remove(row);

            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return 0;
        });

    /// <summary>
    ///     This method stands in for the resume step: it marks the rows succeeded (out of the due
    ///     state) and saves them, without sending anything.
    /// </summary>
    private static async Task ResumeAsync(
        IServiceProvider provider,
        IReadOnlyList<WebhookDeliveryAttemptEntity> due
    )
    {
        IWebhookDeliveryAttemptRepository repository =
            provider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (WebhookDeliveryAttemptEntity attempt in due)
        {
            attempt.Status = WebhookDeliveryAttemptStatusEnum.Succeeded;
            attempt.NextRetryDueUtc = null;
            attempt.CompletedAtUtc = now;
            repository.Update(attempt);
        }

        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>Reads every due row with one call, then resumes them in one scope.</summary>
    [Benchmark(Baseline = true)]
    public Task<int> ReadAllAtOnce() =>
        InScopeAsync(async provider =>
        {
            IReadOnlyList<WebhookDeliveryAttemptEntity> due = await provider
                .GetRequiredService<IWebhookDeliveryAttemptRepository>()
                .ListDueForRetryAsync(DateTimeOffset.UtcNow, int.MaxValue);
            await ResumeAsync(provider, due);
            return due.Count;
        });

    /// <summary>
    ///     Reads batches of 500 due rows, oldest first, each batch read and resumed in its own
    ///     scope, until a batch is short.
    /// </summary>
    [Benchmark]
    public async Task<int> ReadInBatches()
    {
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        int resumed = 0;
        while (true)
        {
            int count = await InScopeAsync(async provider =>
            {
                IReadOnlyList<WebhookDeliveryAttemptEntity> due = await provider
                    .GetRequiredService<IWebhookDeliveryAttemptRepository>()
                    .ListDueForRetryAsync(asOf, 500);
                await ResumeAsync(provider, due);
                return due.Count;
            });

            resumed += count;
            if (count < 500)
                return resumed;
        }
    }
}

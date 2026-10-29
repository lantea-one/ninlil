using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Webhooks.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class tests how <see cref="WebhookDeliveryRetrySweepService.SweepAsync" /> reads a
///     backlog: in batches of <see cref="WebhookDeliveryRetrySweepService.RetryBatchSize" />,
///     every due row resumed in the same sweep, and no row passed to a resume twice. It runs the
///     real <see cref="WebhookDeliveryService" /> over in-memory fakes with no subscriptions, so
///     a resumed row is dead-lettered at once and no request is sent. A counting
///     <see cref="FakeWebhookDeliveryAttemptRepository" /> reports how many reads a sweep made.
/// </summary>
public sealed class WebhookDeliveryRetrySweepBatchTests
{
    private const int BatchSize = WebhookDeliveryRetrySweepService.RetryBatchSize;

    /// <summary>
    ///     This class holds one sweep service and the fakes behind it. The attempt repository is
    ///     one shared instance, so every scope the sweep creates sees the same rows; the scopes
    ///     are counted by <see cref="CountingScopeFactory" />.
    /// </summary>
    private sealed class Harness
    {
        public FakeWebhookDeliveryAttemptRepository Attempts { get; } = new();

        public CountingScopeFactory Scopes { get; }

        public WebhookDeliveryRetrySweepService Service { get; }

        /// <summary>
        ///     This constructor wires the sweep over the fakes.
        /// </summary>
        /// <param name="sweepInterval">The configured sweep interval; a negative one makes a disabled subscription's deferred row stay due.</param>
        /// <param name="subscriptions">The subscriptions that exist; none by default, so a resume dead-letters every row.</param>
        /// <param name="unitOfWork">The unit of work every scope shares.</param>
        public Harness(
            TimeSpan sweepInterval,
            IReadOnlyList<WebhookSubscriptionEntity> subscriptions = null,
            IUnitOfWork unitOfWork = null
        )
        {
            StaticOptionsMonitor<WebhookDeliveryOptions> options = new(
                new WebhookDeliveryOptions { SweepInterval = sweepInterval }
            );

            ServiceCollection services = new();
            services.AddSingleton<IWebhookDeliveryAttemptRepository>(Attempts);
            services.AddSingleton<IWebhookSubscriptionRepository>(
                new FakeWebhookSubscriptionRepository(subscriptions ?? [])
            );
            services.AddSingleton(unitOfWork ?? new FakeUnitOfWork());
            services.AddSingleton(
                new WebhookCredentialProtector(new EphemeralDataProtectionProvider())
            );
            services.AddHttpClient(WebhookDeliveryService.HttpClientName);
            services.AddSingleton<IOptionsMonitor<WebhookDeliveryOptions>>(options);
            services.AddScoped<WebhookDeliveryService>();

            ServiceProvider provider = services.BuildServiceProvider();
            Scopes = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
            Service = new WebhookDeliveryRetrySweepService(
                Scopes,
                options,
                NullLogger<WebhookDeliveryRetrySweepService>.Instance
            );
        }

        /// <summary>
        ///     This method adds <paramref name="count" /> pending rows that are due. Their due
        ///     times rise with the index, so the oldest row has the lowest index.
        /// </summary>
        /// <param name="count">How many rows to add.</param>
        /// <param name="age">How long ago the oldest row fell due.</param>
        /// <param name="subscriptionId">The subscription the rows belong to; no subscription exists, so a resume dead-letters the row.</param>
        public void AddDue(int count, Guid? subscriptionId = null, TimeSpan? age = null)
        {
            DateTimeOffset start = DateTimeOffset.UtcNow - (age ?? TimeSpan.FromHours(2));
            for (int index = 0; index < count; index++)
                Attempts.Items.Add(
                    new WebhookDeliveryAttemptEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = start,
                        SubscriptionId = subscriptionId ?? Guid.NewGuid(),
                        SubscriptionName = "batch-test",
                        EventTypeName = "DnsZoneChanged",
                        PayloadJson = "{}",
                        Status = WebhookDeliveryAttemptStatusEnum.Pending,
                        AttemptCount = 1,
                        MaxAttempts = 5,
                        NextRetryDueUtc = start.AddMilliseconds(index),
                    }
                );
        }
    }

    /// <summary>
    ///     This class forwards to the real scope factory and counts the scopes it creates.
    /// </summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _created;

        /// <summary>Gets the number of scopes created so far.</summary>
        public int Created => Volatile.Read(ref _created);

        /// <inheritdoc />
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _created);
            return inner.CreateScope();
        }
    }

    /// <summary>
    ///     This class is a unit of work that throws on its n-th save, to make a sweep fail part
    ///     way through its batches.
    /// </summary>
    private sealed class ThrowingUnitOfWork(int throwOnCall) : IUnitOfWork
    {
        private int _calls;

        /// <inheritdoc />
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
        {
            if (Interlocked.Increment(ref _calls) == throwOnCall)
                throw new InvalidOperationException("The save always fails.");

            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task SweepAsync_With1203DueRows_ResumesAllOfThemInThreeReads()
    {
        Harness harness = new(TimeSpan.FromSeconds(5));
        harness.AddDue(1203);

        (int resumed, _) = await harness.Service.SweepAsync();

        Assert.Equal(1203, resumed);
        Assert.Equal(3, harness.Attempts.DueReadCount);
        Assert.All(harness.Attempts.DueReadLimits, limit => Assert.Equal(BatchSize, limit));
        Assert.All(
            harness.Attempts.Items,
            attempt => Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, attempt.Status)
        );
    }

    [Fact]
    public async Task SweepAsync_WithNothingDue_ReadsOnce()
    {
        Harness harness = new(TimeSpan.FromSeconds(5));

        (int resumed, _) = await harness.Service.SweepAsync();

        Assert.Equal(0, resumed);
        Assert.Equal(1, harness.Attempts.DueReadCount);
    }

    [Fact]
    public async Task SweepAsync_WithExactlyTwoFullBatchesDue_ReadsAThirdTimeAndStopsOnTheEmptyBatch()
    {
        Harness harness = new(TimeSpan.FromSeconds(5));
        harness.AddDue(2 * BatchSize);

        (int resumed, _) = await harness.Service.SweepAsync();

        Assert.Equal(2 * BatchSize, resumed);
        Assert.Equal(3, harness.Attempts.DueReadCount);
    }

    [Fact]
    public async Task SweepAsync_ReadsEachBatchInAScopeOfItsOwn()
    {
        Harness harness = new(TimeSpan.FromSeconds(5));
        harness.AddDue(1203);

        await harness.Service.SweepAsync();

        // Three batches plus the retention cleanup. A scope per batch drops the change tracking
        // of the rows already resumed before the next read.
        Assert.Equal(4, harness.Scopes.Created);
    }

    [Fact]
    public async Task SweepAsync_WhenAResumeLeavesTheRowsDue_DoesNotReadThemAgainInTheSameSweep()
    {
        // A negative interval makes a disabled subscription's deferred row fall due an hour ago,
        // so a resume does not move it out of the due state. The untouched rows fell due ten
        // minutes ago, so the deferred rows are the oldest and come back first.
        WebhookSubscriptionEntity disabled = DisabledSubscription();
        Harness harness = new(TimeSpan.FromHours(-1), [disabled]);
        harness.AddDue(BatchSize + 100, disabled.Id, TimeSpan.FromMinutes(10));

        (int resumed, _) = await harness.Service.SweepAsync();

        // The first batch (the 500 oldest) is deferred but still due and comes back first as
        // the whole second batch; it is all seen, so the sweep stops. The 100 rows behind it wait
        // for the next sweep.
        Assert.Equal(BatchSize, resumed);
        Assert.Equal(2, harness.Attempts.DueReadCount);
    }

    [Fact]
    public async Task SweepAsync_WhenAResumeLeavesTheRowsDueBehindOlderOnes_ResumesEachRowOnce()
    {
        // The untouched rows fell due two hours ago, before the deferral time of an hour ago, so
        // the 100 rows left after the first batch come before the deferred ones.
        WebhookSubscriptionEntity disabled = DisabledSubscription();
        Harness harness = new(TimeSpan.FromHours(-1), [disabled]);
        harness.AddDue(BatchSize + 100, disabled.Id);

        (int resumed, _) = await harness.Service.SweepAsync();

        // Read 2 holds the 100 untouched rows and 400 deferred ones; only the 100 are new. Read 3
        // holds 500 deferred rows, all seen.
        Assert.Equal(BatchSize + 100, resumed);
        Assert.Equal(3, harness.Attempts.DueReadCount);
    }

    [Fact]
    public async Task SweepAsync_WhenFewerThanABatchStayDue_ReadsOnce()
    {
        WebhookSubscriptionEntity disabled = DisabledSubscription();
        Harness harness = new(TimeSpan.FromHours(-1), [disabled]);
        harness.AddDue(300, disabled.Id);

        (int resumed, _) = await harness.Service.SweepAsync();

        Assert.Equal(300, resumed);
        Assert.Equal(1, harness.Attempts.DueReadCount);
    }

    [Fact]
    public async Task SweepAsync_WhenASaveFailsInTheSecondBatch_ThrowsWithoutReadingAgain()
    {
        Harness harness = new(
            TimeSpan.FromSeconds(5),
            unitOfWork: new ThrowingUnitOfWork(throwOnCall: 2)
        );
        harness.AddDue(3 * BatchSize);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.SweepAsync());

        Assert.Equal(2, harness.Attempts.DueReadCount);
    }

    private static WebhookSubscriptionEntity DisabledSubscription() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "disabled",
            TargetUrl = "http://127.0.0.1:1/hook",
            EventTypeNames = ["DnsZoneChanged"],
            IsDisabled = true,
        };
}

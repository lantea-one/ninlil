using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.Services;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests the start/stop bookkeeping of <see cref="ManagedServiceRegistration" />
///     with a fake <see cref="IHostedService" />: forwarding of start and stop, the
///     <c>isAlreadyRunning</c> seed, and a running probe taking precedence. The seed matters
///     because the host has already started every wrapped service before the registry is built;
///     without it, <c>GET /api/services</c> would report every background service as stopped
///     after boot.
/// </summary>
public sealed class ManagedServiceRegistrationTests
{
    /// <summary>
    ///     This class is a fake <see cref="IHostedService" /> that records start and stop calls, so
    ///     a test can check the registration forwards them rather than only updating its
    ///     bookkeeping.
    /// </summary>
    private sealed class FakeHostedService : IHostedService
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AFreshRegistrationWithNoIsAlreadyRunningArgument_ReportsNotRunning()
    {
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            new FakeHostedService()
        );

        Assert.False(registration.IsRunning);
    }

    [Fact]
    public void ARegistrationConstructedWithIsAlreadyRunningTrue_ReportsRunningImmediately()
    {
        // As in production: the host already started the service before it was wrapped, so
        // ManagedServiceRegistry passes true.
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            new FakeHostedService(),
            isAlreadyRunning: true
        );

        Assert.True(registration.IsRunning);
    }

    [Fact]
    public async Task StopAsync_OnAnAlreadyRunningRegistration_ForwardsToTheWrappedServiceAndFlipsIsRunning()
    {
        FakeHostedService fake = new();
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            fake,
            isAlreadyRunning: true
        );

        await registration.StopAsync(CancellationToken.None);

        Assert.Equal(1, fake.StopCount);
        Assert.False(registration.IsRunning);
    }

    [Fact]
    public async Task StartAsync_AfterStop_ForwardsToTheWrappedServiceAndFlipsIsRunningBack()
    {
        FakeHostedService fake = new();
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            fake,
            isAlreadyRunning: true
        );

        await registration.StopAsync(CancellationToken.None);
        await registration.StartAsync(CancellationToken.None);

        Assert.Equal(1, fake.StartCount);
        Assert.True(registration.IsRunning);
    }

    [Fact]
    public void ARicherIsRunningProbe_OverridesTheBookkeepingFallbackRegardlessOfIsAlreadyRunning()
    {
        bool probeState = false;

        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Transport,
            new FakeHostedService(),
            () => probeState,
            isAlreadyRunning: true
        );

        // isAlreadyRunning: true alone would report running, but a probe always wins: the
        // "enabled but declined to bind" transport case.
        Assert.False(registration.IsRunning);

        probeState = true;
        Assert.True(registration.IsRunning);
    }

    /// <summary>
    ///     This class is a fake <see cref="BackgroundService" /> that counts how many
    ///     <c>ExecuteAsync</c> loops have started and how many run at once, so a test can prove a
    ///     repeated start never gives the service a second loop.
    /// </summary>
    private sealed class CountingBackgroundService : BackgroundService
    {
        private int current;
        private int executions;
        private int maxConcurrent;

        public int Executions => Volatile.Read(ref executions);
        public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref executions);
            int now = Interlocked.Increment(ref current);
            int seen;

            while (now > (seen = Volatile.Read(ref maxConcurrent)))
                Interlocked.CompareExchange(ref maxConcurrent, now, seen);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) { }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }
    }

    [Fact]
    public async Task StartAsync_OnAServiceThatIsRunning_DoesNotStartItAgain()
    {
        CountingBackgroundService service = new();
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            service
        );

        await registration.StartAsync(CancellationToken.None);
        await registration.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Assert.True(registration.IsRunning);
        Assert.Equal(1, service.Executions);
        Assert.Equal(1, service.MaxConcurrent);
        await registration.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_CalledConcurrently_StartsTheServiceOnce()
    {
        CountingBackgroundService service = new();
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            service
        );

        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() => registration.StartAsync(CancellationToken.None)))
        );
        await Task.Delay(100);

        Assert.Equal(1, service.Executions);
        Assert.Equal(1, service.MaxConcurrent);
        await registration.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_AfterStop_StartsTheServiceAgainWithoutTwoLoopsAtOnce()
    {
        CountingBackgroundService service = new();
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Background,
            service
        );

        await registration.StartAsync(CancellationToken.None);

        // BackgroundService runs ExecuteAsync off the start call's thread, so wait until the
        // first loop has begun; a stop before that would cancel it before it ever ran.
        while (service.Executions < 1)
            await Task.Delay(10);

        await registration.StopAsync(CancellationToken.None);
        await registration.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Assert.True(registration.IsRunning);
        Assert.Equal(2, service.Executions);
        Assert.Equal(1, service.MaxConcurrent);
        await registration.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_OnAStartedServiceWhoseProbeSaysDown_StopsThenStartsIt()
    {
        CountingBackgroundService service = new();
        ManagedServiceRegistration registration = new(
            "fake",
            ManagedServiceCategoryEnum.Transport,
            service,
            () => false
        );

        // The probe stays false, so each start waits out its poll timeout (2 seconds).
        await registration.StartAsync(CancellationToken.None);
        await registration.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Assert.Equal(2, service.Executions);
        Assert.Equal(1, service.MaxConcurrent);
        await registration.StopAsync(CancellationToken.None);
    }
}

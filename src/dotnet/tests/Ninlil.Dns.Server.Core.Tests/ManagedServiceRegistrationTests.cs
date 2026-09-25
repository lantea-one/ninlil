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
}

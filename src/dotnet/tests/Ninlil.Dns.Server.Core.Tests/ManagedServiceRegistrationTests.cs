using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.Services;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class covers <see cref="ManagedServiceRegistration" />'s own start/stop bookkeeping,
///     including a real bug the Service Lifecycle Management registry-wiring phase found and
///     fixed: a fresh registration used to always report <see cref="ManagedServiceRegistration.IsRunning" />
///     as <see langword="false" /> until its own <see cref="ManagedServiceRegistration.StartAsync" />
///     was called at least once -- correct for a genuinely fresh, not-yet-started component, but
///     wrong for the real shape every service this project's own composition root actually wraps:
///     the ASP.NET Core generic host already called the wrapped <see cref="IHostedService" />'s own
///     <c>StartAsync</c> directly, once, at process startup, well before any registry is ever built
///     to wrap it -- so <c>GET /api/services</c> would have incorrectly reported every background
///     service (every one with no richer <c>IsListening</c>-style probe of its own) as not running
///     immediately after a real boot, until an operator happened to call this wrapper's own
///     <c>StartAsync</c> at least once. The new <c>isAlreadyRunning</c> constructor parameter fixes
///     this by letting the real composition root seed the correct initial state.
/// </summary>
public sealed class ManagedServiceRegistrationTests
{
    /// <summary>
    ///     A minimal fake <see cref="IHostedService" /> recording whether it was actually started/
    ///     stopped, so a test can assert this registration forwards its own calls through rather
    ///     than only updating its own bookkeeping.
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
        // This is the real-world shape: the generic host already started the wrapped service
        // directly, before this registration ever wraps it -- ManagedServiceRegistry.Build passes
        // true for exactly this reason for every service with no richer probe of its own.
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

        // isAlreadyRunning: true would otherwise report IsRunning as true, but a supplied probe
        // always wins -- exactly the "a transport that is enabled but declined to bind" case
        // IManagedServiceRegistration's own doc comment describes.
        Assert.False(registration.IsRunning);

        probeState = true;
        Assert.True(registration.IsRunning);
    }
}

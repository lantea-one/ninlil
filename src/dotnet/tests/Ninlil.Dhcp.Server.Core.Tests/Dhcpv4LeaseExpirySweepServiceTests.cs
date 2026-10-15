using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers <see cref="Dhcpv4LeaseExpirySweepService" />, which marks expired
///     active leases and publishes their events, by calling <c>SweepAsync</c> directly on a
///     <see cref="Dhcpv4EngineTestHost" /> with a temporary SQLite file. The loop tests start the
///     service itself over a scope factory whose lease repository always fails, and the race test
///     wraps the real repository so a renewal commits right after the sweep has listed the row.
/// </summary>
public sealed class Dhcpv4LeaseExpirySweepServiceTests : IAsyncLifetime
{
    private Dhcpv4EngineTestHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv4EngineTestHost();
        await _host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task SweepAsync_ExpiresAnOverdueActiveLease_AndPublishesExpired()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "sweep-scope",
            "192.0.2.140",
            "192.0.2.140"
        );
        LeaseEntity lease = await SeedActiveLeaseAsync(
            scope.Name,
            "05:05:05:05:05:05",
            "192.0.2.140",
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );

        Dhcpv4LeaseExpirySweepService sweepService = GetSweepService();

        int expiredCount = await sweepService.SweepAsync();

        Assert.Equal(1, expiredCount);

        LeaseEntity reloaded = await _host.FindLeaseAsync(scope.Name, "05:05:05:05:05:05");
        Assert.Equal(LeaseStateEnum.Expired, reloaded.State);

        DhcpLeaseChangedEvent published = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Expired, published.ChangeKind);
        Assert.Equal(lease.Id, published.LeaseId);
    }

    [Fact]
    public async Task SweepAsync_LeavesAnActiveLeaseNotYetOverdueAlone()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "sweep-scope-2",
            "192.0.2.141",
            "192.0.2.141"
        );
        await SeedActiveLeaseAsync(
            scope.Name,
            "06:06:06:06:06:06",
            "192.0.2.141",
            DateTimeOffset.UtcNow.AddHours(1)
        );

        Dhcpv4LeaseExpirySweepService sweepService = GetSweepService();

        int expiredCount = await sweepService.SweepAsync();

        Assert.Equal(0, expiredCount);
        Assert.Empty(_host.PublishedEvents);

        LeaseEntity reloaded = await _host.FindLeaseAsync(scope.Name, "06:06:06:06:06:06");
        Assert.Equal(LeaseStateEnum.Active, reloaded.State);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEverySweepFails_WaitsTheIntervalAndLogsOneWarningPerAttempt()
    {
        int attempts = 0;
        CountingLoggerProvider loggerProvider = new();
        ServiceProvider provider = BuildFailingProvider(() => Interlocked.Increment(ref attempts));
        Dhcpv4LeaseExpirySweepService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(TimeSpan.FromMilliseconds(200)),
            loggerProvider.CreateLogger<Dhcpv4LeaseExpirySweepService>()
        );

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await service.StopAsync(CancellationToken.None);

        Assert.InRange(attempts, 1, 7);
        Assert.Equal(attempts, loggerProvider.Warnings);
    }

    [Fact]
    public async Task ExecuteAsync_WithANonPositiveInterval_StillWaitsAndWarnsAboutItOnce()
    {
        int attempts = 0;
        CountingLoggerProvider loggerProvider = new();
        ServiceProvider provider = BuildFailingProvider(() => Interlocked.Increment(ref attempts));
        Dhcpv4LeaseExpirySweepService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(TimeSpan.Zero),
            loggerProvider.CreateLogger<Dhcpv4LeaseExpirySweepService>()
        );

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await service.StopAsync(CancellationToken.None);

        // The fallback interval is one minute, so only the startup sweep ran: one failure
        // warning plus the single fallback warning.
        Assert.Equal(1, attempts);
        Assert.Equal(2, loggerProvider.Warnings);
    }

    [Fact]
    public async Task SweepAsync_ALeaseRenewedAfterTheListing_StaysActiveAndPublishesNothing()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "sweep-scope-race",
            "192.0.2.142",
            "192.0.2.142"
        );
        await SeedActiveLeaseAsync(
            scope.Name,
            "07:07:07:07:07:07",
            "192.0.2.142",
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );
        DateTimeOffset renewedExpiry = DateTimeOffset.UtcNow.AddHours(1);

        Dhcpv4LeaseExpirySweepService service = new(
            new InterceptingScopeFactory(
                _host.Host.Services.GetRequiredService<IServiceScopeFactory>(),
                async method =>
                {
                    // The renewal commits through its own scope between the sweep's load and write.
                    if (method.Name == nameof(ILeaseRepository.ListAsync))
                        await RenewAsync(scope.Name, "07:07:07:07:07:07", renewedExpiry);
                }
            ),
            new StaticOptionsMonitor(TimeSpan.FromMinutes(1)),
            new CountingLoggerProvider().CreateLogger<Dhcpv4LeaseExpirySweepService>()
        );

        int expiredCount = await service.SweepAsync();

        Assert.Equal(0, expiredCount);
        Assert.Empty(_host.PublishedEvents);

        LeaseEntity reloaded = await _host.FindLeaseAsync(scope.Name, "07:07:07:07:07:07");
        Assert.Equal(LeaseStateEnum.Active, reloaded.State);
        Assert.True(reloaded.LeaseExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(30));
    }

    private async Task RenewAsync(
        string scopeName,
        string clientIdentifier,
        DateTimeOffset expiresAtUtc
    )
    {
        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        ILeaseRepository repository = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        LeaseEntity lease = await repository.FindByClientIdentifierAsync(
            scopeName,
            clientIdentifier
        );
        lease.LeaseExpiresAtUtc = expiresAtUtc;
        repository.Update(lease);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method builds a provider whose lease repository throws on every call, counting
    ///     the calls through <paramref name="onCall" />.
    /// </summary>
    private static ServiceProvider BuildFailingProvider(Action onCall)
    {
        ServiceCollection services = new();
        services.AddScoped(_ =>
        {
            ILeaseRepository proxy = DispatchProxy.Create<ILeaseRepository, DelegateProxy>();
            ((DelegateProxy)(object)proxy).Handler = (_, _) =>
            {
                onCall();
                throw new InvalidOperationException("The database is unavailable.");
            };
            return proxy;
        });
        services.AddSingleton<IDnsServerEventPublisher, NullEventPublisher>();
        return services.BuildServiceProvider();
    }

    private async Task<LeaseEntity> SeedActiveLeaseAsync(
        string scopeName,
        string clientIdentifier,
        string ipAddress,
        DateTimeOffset leaseExpiresAtUtc
    )
    {
        LeaseEntity lease = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = scopeName,
            ClientIdentifier = clientIdentifier,
            IpAddress = ipAddress,
            State = LeaseStateEnum.Active,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
        };

        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        ILeaseRepository repository = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        await repository.AddAsync(lease);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return lease;
    }

    /// <summary>
    ///     This method finds the <see cref="Dhcpv4LeaseExpirySweepService" /> among the registered
    ///     <see cref="IHostedService" /> instances, since <c>AddHostedService</c> does not register
    ///     it under its own type, as
    ///     <c>Ninlil.Dns.Server.Webhooks.Tests.WebhookDeliveryRetrySweepServiceTests.GetSweepService</c>
    ///     does.
    /// </summary>
    private Dhcpv4LeaseExpirySweepService GetSweepService() =>
        _host
            .Host.Services.GetServices<IHostedService>()
            .OfType<Dhcpv4LeaseExpirySweepService>()
            .Single();

    /// <summary>This class is a dispatch proxy that hands every call to <see cref="Handler" />.</summary>
    public class DelegateProxy : DispatchProxy
    {
        /// <summary>This property is the function that answers each call.</summary>
        public Func<MethodInfo, object[], object> Handler { get; set; }

        /// <inheritdoc />
        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            Handler(targetMethod, args);
    }

    /// <summary>
    ///     This class creates real scopes from the host but wraps the <see cref="ILeaseRepository" />
    ///     they resolve, so <c>onCompleted</c> runs after each of its asynchronous methods finishes.
    /// </summary>
    private sealed class InterceptingScopeFactory(
        IServiceScopeFactory inner,
        Func<MethodInfo, Task> onCompleted
    ) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            IServiceScope scope = inner.CreateScope();
            ILeaseRepository real = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            ILeaseRepository proxy = DispatchProxy.Create<ILeaseRepository, DelegateProxy>();
            ((DelegateProxy)(object)proxy).Handler = (method, args) =>
            {
                object result = method.Invoke(real, args);
                if (result is not Task task || !method.ReturnType.IsGenericType)
                    return result;

                return typeof(InterceptingScopeFactory)
                    .GetMethod(nameof(After), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0])
                    .Invoke(null, [task, method, onCompleted]);
            };
            return new InterceptedScope(scope, proxy);
        }

        private static async Task<T> After<T>(
            Task<T> task,
            MethodInfo method,
            Func<MethodInfo, Task> onCompleted
        )
        {
            T value = await task;
            await onCompleted(method);
            return value;
        }
    }

    private sealed class InterceptedScope(IServiceScope inner, ILeaseRepository repository)
        : IServiceScope,
            IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public object GetService(Type serviceType) =>
            serviceType == typeof(ILeaseRepository)
                ? repository
                : inner.ServiceProvider.GetService(serviceType);

        public void Dispose() => inner.Dispose();
    }

    private sealed class NullEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private sealed class StaticOptionsMonitor(TimeSpan interval)
        : IOptionsMonitor<DhcpServerOptions>
    {
        public DhcpServerOptions CurrentValue { get; } =
            new() { LeaseExpirySweepInterval = interval };

        public DhcpServerOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<DhcpServerOptions, string> listener) => null;
    }

    /// <summary>This class counts the warnings written to the loggers it creates.</summary>
    private sealed class CountingLoggerProvider
    {
        private int _warnings;

        public int Warnings => Volatile.Read(ref _warnings);

        public ILogger<T> CreateLogger<T>() => new CountingLogger<T>(this);

        private sealed class CountingLogger<T>(CountingLoggerProvider owner) : ILogger<T>
        {
            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            )
            {
                if (logLevel == LogLevel.Warning)
                    Interlocked.Increment(ref owner._warnings);
            }
        }
    }
}

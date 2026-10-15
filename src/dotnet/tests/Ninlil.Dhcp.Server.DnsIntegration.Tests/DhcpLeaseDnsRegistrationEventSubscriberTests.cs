using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.DnsIntegration.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class covers <see cref="DhcpLeaseDnsRegistrationEventSubscriber" />: lease events are
///     handled one at a time. The subscriber is built from the provider that
///     <see cref="ServiceCollectionDhcpDnsIntegrationExtension.AddNinlilDhcpDnsIntegration" />
///     fills, so it gets whatever the production wiring gives it; the lease repository is a
///     probe that delays the first read of every handling and records how many handlings are
///     inside it at once.
/// </summary>
public sealed class DhcpLeaseDnsRegistrationEventSubscriberTests
{
    [Fact]
    public async Task HandleAsync_TwoEventsForOneLease_AreHandledOneAtATime()
    {
        LeaseReadProbe probe = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddNinlilDhcpDnsIntegration();
        services.AddScoped(_ => new DhcpDnsRegistrationService(
            Probe(probe),
            null,
            null,
            null,
            null,
            NullLogger<DhcpDnsRegistrationService>.Instance
        ));
        await using ServiceProvider provider = services.BuildServiceProvider();
        DhcpLeaseDnsRegistrationEventSubscriber subscriber =
            ActivatorUtilities.CreateInstance<DhcpLeaseDnsRegistrationEventSubscriber>(provider);
        Guid leaseId = Guid.NewGuid();

        foreach (
            DhcpLeaseChangeKindEnum kind in new[]
            {
                DhcpLeaseChangeKindEnum.Granted,
                DhcpLeaseChangeKindEnum.Expired,
            }
        )
            await subscriber.HandleAsync(
                new DhcpLeaseChangedEvent(DateTimeOffset.UtcNow, leaseId, kind),
                CancellationToken.None
            );

        for (int attempt = 0; attempt < 100 && probe.Completed < 2; attempt++)
            await Task.Delay(50);

        Assert.Equal(2, probe.Completed);
        Assert.Equal(1, probe.MaxConcurrent);
    }

    private static ILeaseRepository Probe(LeaseReadProbe probe)
    {
        ILeaseRepository proxy = DispatchProxy.Create<
            ILeaseRepository,
            LeaseRepositoryProbeProxy
        >();
        ((LeaseRepositoryProbeProxy)(object)proxy).Probe = probe;
        return proxy;
    }

    /// <summary>
    ///     This class counts the handlings that are inside the probe repository's read.
    /// </summary>
    public sealed class LeaseReadProbe
    {
        private int _current;
        private int _max;
        private int _completed;

        public int MaxConcurrent => Volatile.Read(ref _max);

        public int Completed => Volatile.Read(ref _completed);

        public async Task<LeaseEntity> ReadAsync()
        {
            int now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)))
                Interlocked.CompareExchange(ref _max, now, seen);

            await Task.Delay(200);
            Interlocked.Decrement(ref _current);
            Interlocked.Increment(ref _completed);

            // No lease: the service then skips with "The lease no longer exists" and touches
            // none of its other dependencies.
            return null;
        }
    }

    /// <summary>
    ///     This class answers <c>GetByIdAsync</c> through the probe.
    /// </summary>
    public class LeaseRepositoryProbeProxy : DispatchProxy
    {
        public LeaseReadProbe Probe { get; set; }

        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            targetMethod.Name == "GetByIdAsync"
                ? Probe.ReadAsync()
                : throw new NotSupportedException(targetMethod.Name);
    }
}

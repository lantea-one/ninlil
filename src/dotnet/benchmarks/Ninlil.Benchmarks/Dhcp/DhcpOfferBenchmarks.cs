using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Dns;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Benchmarks.Dhcp;

/// <summary>
///     This class measures <see cref="IDhcpv4LeaseAllocationService.OfferAsync" />, the work a
///     DHCPDISCOVER costs in the database, on a seeded scope (a quarter of the data set's leases,
///     1,250 at the large scale). The service lists every lease of the scope on each call, so
///     this is the baseline for the later packet-path work. The benchmark client has a lease
///     after the first offer, so every measured call takes the sticky path: list the scope, find
///     the client's row, update it, save. <c>OnCleanupAsync</c> removes the row.
/// </summary>
[Config(typeof(StorageConfig))]
public class DhcpOfferBenchmarks : DnsPathBenchmarkBase
{
    private static readonly byte[] HardwareAddress = [0x02, 0xBE, 0x4C, 0x00, 0x00, 0x01];

    private DhcpScopeEntity _scope;
    private Dhcpv4Message _discover;

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);

        // A fixed server identifier keeps the service from looking at the machine's interfaces.
        services.AddSingleton<IDnsServerEventPublisher, DiscardingEventPublisher>();
        services.AddNinlilDhcpServerCore(configuration);
        services.Configure<DhcpServerOptions>(options =>
            options.ServerIdentifierAddress = "192.0.2.1"
        );
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _scope = await InScopeAsync(provider =>
            provider
                .GetRequiredService<IDhcpScopeRepository>()
                .FindByNameAsync($"{BenchmarkDataSet.ScopeNamePrefix}0")
        );
        Expect(_scope is not null, "the seeded scope exists");

        _discover = new Dhcpv4Message
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x42,
            HardwareAddressLength = (byte)HardwareAddress.Length,
            ClientHardwareAddress = HardwareAddress,
        };

        LeaseEntity first = await Offer();
        Expect(first is { State: LeaseStateEnum.Offered }, "a new client is offered an address");
        LeaseEntity second = await Offer();
        Expect(
            second is { State: LeaseStateEnum.Offered } && second.IpAddress == first.IpAddress,
            "the same client is offered the same address again"
        );
    }

    /// <inheritdoc />
    protected override async Task OnCleanupAsync() =>
        await InScopeAsync(async provider =>
        {
            ILeaseRepository leases = provider.GetRequiredService<ILeaseRepository>();
            LeaseEntity lease = await leases.FindByClientIdentifierAsync(
                _scope.Name,
                "02:be:4c:00:00:01"
            );

            if (lease is not null)
            {
                leases.Remove(lease);
                await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            return true;
        });

    /// <summary>An offer to a client that already holds an offered lease.</summary>
    [Benchmark]
    public async Task<string> OfferToKnownClient() => (await Offer()).IpAddress;

    private Task<LeaseEntity> Offer() =>
        InScopeAsync(provider =>
            provider
                .GetRequiredService<IDhcpv4LeaseAllocationService>()
                .OfferAsync(_scope, _discover)
        );

    private sealed class DiscardingEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}

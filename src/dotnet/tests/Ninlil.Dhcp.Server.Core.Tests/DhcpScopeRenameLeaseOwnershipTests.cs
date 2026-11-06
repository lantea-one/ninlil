using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the rule that a lease belongs to its scope by the scope's id, not
///     by its name. Each test hands out a lease, renames the scope with a plain scope save (no
///     lease is touched, so nothing repairs a name link) and checks that the DHCP engine still
///     renews the same lease row. It runs the real allocation service over the SQLite file of
///     <see cref="Dhcpv4EngineTestHost" />. The DNS side of the same rule is covered in
///     <c>Ninlil.Dhcp.Server.DnsIntegration.Tests.DhcpDnsRegistrationServiceTests</c>.
/// </summary>
public sealed class DhcpScopeRenameLeaseOwnershipTests : IAsyncLifetime
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
    public async Task ConfirmAsync_AfterTheScopeIsRenamed_RenewsTheSameLease()
    {
        DhcpScopeEntity seeded = await _host.SeedScopeAsync(
            "ownership-before",
            "192.0.2.10",
            "192.0.2.20"
        );
        Dhcpv4Message request = NewRequest([0xc1, 0, 0, 0, 0, 1]);

        Guid leaseId;
        await using (AsyncServiceScope first = _host.Host.Services.CreateAsyncScope())
        {
            IDhcpv4LeaseAllocationService service =
                first.ServiceProvider.GetRequiredService<IDhcpv4LeaseAllocationService>();
            LeaseEntity offered = await service.OfferAsync(seeded, request);
            LeaseEntity active = await service.ConfirmAsync(
                seeded,
                request,
                IPAddress.Parse(offered.IpAddress),
                TimeSpan.FromHours(1)
            );
            Assert.Equal(LeaseStateEnum.Active, active.State);
            leaseId = active.Id;
        }

        // The rename changes only the scope row, as a write to the scope table alone would.
        DhcpScopeEntity renamed;
        await using (AsyncServiceScope rename = _host.Host.Services.CreateAsyncScope())
        {
            IDhcpScopeRepository scopes =
                rename.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
            renamed = await scopes.GetByIdAsync(seeded.Id);
            renamed.Name = "ownership-after";
            scopes.Update(renamed);
            await rename.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope renew = _host.Host.Services.CreateAsyncScope();
        LeaseEntity renewed = await renew
            .ServiceProvider.GetRequiredService<IDhcpv4LeaseAllocationService>()
            .ConfirmAsync(renamed, request, IPAddress.Parse("192.0.2.10"), TimeSpan.FromHours(2));

        Assert.NotNull(renewed);
        Assert.Equal(leaseId, renewed.Id);

        ILeaseRepository leases = renew.ServiceProvider.GetRequiredService<ILeaseRepository>();
        IReadOnlyList<LeaseEntity> rows = await leases.SearchAsync(0, 100);
        Assert.Single(rows, row => row.IpAddress == "192.0.2.10");
    }

    private static Dhcpv4Message NewRequest(byte[] clientHardwareAddress)
    {
        Dhcpv4Message message = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x43,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
        message.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        return message;
    }
}

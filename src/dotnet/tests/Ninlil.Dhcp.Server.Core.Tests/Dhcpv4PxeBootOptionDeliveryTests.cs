using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers PXE boot delivery (<c>Dhcpv4OptionDeliveryService.ApplyPxeBootOptions</c>):
///     <see cref="DhcpScopeEntity.BootFileName" /> and <see cref="DhcpScopeEntity.NextServerAddress" />
///     always fill the "file" and "siaddr" header fields of an OFFER or ACK, and the RFC 2132
///     §9.4 and §9.5 TFTP Server Name and Bootfile Name options follow the Parameter Request
///     List. Each test calls <see cref="Dhcpv4RequestHandler.HandleRequestAsync" /> on a
///     <see cref="Dhcpv4EngineTestHost" />.
/// </summary>
public sealed class Dhcpv4PxeBootOptionDeliveryTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

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
    public async Task Discover_WithBootFileNameAndExplicitNextServer_SetsFixedHeaderFields()
    {
        await _host.SeedScopeAsync(
            "pxe-scope",
            "192.0.2.150",
            "192.0.2.152",
            bootFileName: "pxelinux.0",
            nextServerAddress: "192.0.2.200"
        );

        Dhcpv4Message discover = NewMessage([0x0a, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a], 0x1000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal("pxelinux.0", offer.BootFileName);
        Assert.Equal(IPAddress.Parse("192.0.2.200"), offer.ServerIpAddress);
    }

    [Fact]
    public async Task Discover_WithBootFileNameAndNoExplicitNextServer_FallsBackToServerIdentifier()
    {
        await _host.SeedScopeAsync(
            "pxe-fallback-scope",
            "192.0.2.160",
            "192.0.2.162",
            bootFileName: "undionly.kpxe"
        );

        Dhcpv4Message discover = NewMessage([0x0b, 0x0b, 0x0b, 0x0b, 0x0b, 0x0b], 0x1001);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal("undionly.kpxe", offer.BootFileName);
        Assert.Equal(
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress),
            offer.ServerIpAddress
        );
    }

    [Fact]
    public async Task Discover_WithNoBootFileNameConfigured_LeavesFixedFieldsEmpty()
    {
        await _host.SeedScopeAsync("no-pxe-scope", "192.0.2.170", "192.0.2.172");

        Dhcpv4Message discover = NewMessage([0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c], 0x1002);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal(string.Empty, offer.BootFileName);
        Assert.Equal(IPAddress.Any, offer.ServerIpAddress);
    }

    [Fact]
    public async Task Discover_RequestingTag67And66_AlsoReceivesTheOptionForm()
    {
        await _host.SeedScopeAsync(
            "pxe-option-scope",
            "192.0.2.180",
            "192.0.2.182",
            bootFileName: "ipxe.efi",
            nextServerAddress: "192.0.2.201"
        );

        Dhcpv4Message discover = NewMessage([0x0d, 0x0d, 0x0d, 0x0d, 0x0d, 0x0d], 0x1003);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.SetParameterRequestList([66, 67]);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetBootfileNameOption(out string bootfileNameOption));
        Assert.Equal("ipxe.efi", bootfileNameOption);
        Assert.True(offer.TryGetTftpServerNameOption(out string tftpServerNameOption));
        Assert.Equal("192.0.2.201", tftpServerNameOption);
    }

    [Fact]
    public async Task Discover_WithAParameterRequestListOmittingBootOptions_StillGetsTheFixedFields()
    {
        // The "file" and "siaddr" header fields (RFC 2131 §2) are always filled, while options
        // 66 and 67 are left out because the Parameter Request List does not ask for them.
        await _host.SeedScopeAsync(
            "pxe-prl-scope",
            "192.0.2.190",
            "192.0.2.192",
            bootFileName: "grub.efi",
            nextServerAddress: "192.0.2.202"
        );

        Dhcpv4Message discover = NewMessage([0x0e, 0x0e, 0x0e, 0x0e, 0x0e, 0x0e], 0x1004);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.SetParameterRequestList([1, 3, 6]);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal("grub.efi", offer.BootFileName);
        Assert.Equal(IPAddress.Parse("192.0.2.202"), offer.ServerIpAddress);
        Assert.False(offer.TryGetBootfileNameOption(out _));
        Assert.False(offer.TryGetTftpServerNameOption(out _));
    }

    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
}

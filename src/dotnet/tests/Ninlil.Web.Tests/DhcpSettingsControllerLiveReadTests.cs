using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Ra;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks which settings the GET actions of <see cref="DhcpSettingsController" />
///     read live. The Router Advertisement content and the lease expiry sweep interval apply as
///     soon as the configuration file changes, so <c>GET ra</c> and the sweep interval of
///     <c>GET core</c> must show the current value, while the other core fields show what the
///     process started with. The shared test host does not reload its runtime settings file, so
///     the test builds the controller directly with option monitors whose current value differs
///     from the startup value.
/// </summary>
public class DhcpSettingsControllerLiveReadTests
{
    [Fact]
    public void GetRa_ShowsTheValueTheOptionsMonitorHoldsNow()
    {
        RaOptions current = new() { CurrentHopLimit = 17, RouterLifetime = TimeSpan.FromHours(1) };
        DhcpSettingsController controller = CreateController(
            new DhcpServerOptions(),
            new DhcpServerOptions(),
            current
        );

        DhcpRaSettingsDetail detail =
            Assert.IsType<OkObjectResult>(controller.GetRa().Result).Value as DhcpRaSettingsDetail;

        Assert.Equal(17, detail.CurrentHopLimit);
        Assert.Equal(TimeSpan.FromHours(1), detail.RouterLifetime);
    }

    [Fact]
    public void GetCore_ShowsTheCurrentSweepIntervalAndTheStartupValueOfEveryOtherField()
    {
        DhcpServerOptions startup = new()
        {
            OfferHoldDuration = TimeSpan.FromSeconds(30),
            LeaseExpirySweepInterval = TimeSpan.FromMinutes(1),
        };
        DhcpServerOptions current = new()
        {
            OfferHoldDuration = TimeSpan.FromSeconds(99),
            LeaseExpirySweepInterval = TimeSpan.FromMinutes(5),
        };
        DhcpSettingsController controller = CreateController(startup, current, new RaOptions());

        DhcpCoreSettingsDetail detail =
            Assert.IsType<OkObjectResult>(controller.GetCore().Result).Value
            as DhcpCoreSettingsDetail;

        Assert.Equal(TimeSpan.FromMinutes(5), detail.LeaseExpirySweepInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.OfferHoldDuration);
    }

    /// <summary>
    ///     This method builds the controller with the given core options as the startup value,
    ///     the given core options as the live value, and the given Router Advertisement options
    ///     as the live value. The other sections are never read by these tests.
    /// </summary>
    private static DhcpSettingsController CreateController(
        DhcpServerOptions startupCore,
        DhcpServerOptions liveCore,
        RaOptions liveRa
    ) =>
        new(
            Microsoft.Extensions.Options.Options.Create(startupCore),
            new FixedMonitor<DhcpServerOptions>(liveCore),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new TftpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new RaProtocolOptions()),
            new FixedMonitor<RaOptions>(liveRa),
            new RuntimeConfigurationOptions()
        );

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> fake that always returns the
    ///     same value.
    /// </summary>
    private sealed class FixedMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener) => null;
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Ra;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Dns.Common.Exceptions;
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
///     process started with. The PATCH actions that check one field against another (the relay
///     circuit and remote IDs, the Router Advertisement intervals) must read the unpatched field
///     from the monitor too, because an earlier patch saved before the restart is not in the
///     startup options. The shared test host does not reload its runtime settings file, so the
///     tests build the controller directly with option monitors whose current value differs from
///     the startup value.
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

    [Fact]
    public async Task PatchRelay_RemoteIdThatOverflowsTheSavedCircuitId_IsRefused()
    {
        // The saved circuit ID is 200 characters, the startup one is empty: 200 + 60 breaks the
        // combined limit only against the saved value.
        DhcpSettingsController controller = CreateRelayController(
            startup: new Dhcpv4RelayOptions(),
            live: new Dhcpv4RelayOptions { RelayAgentCircuitId = new string('a', 200) }
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.PatchRelay(ToJson(new { relayAgentRemoteId = new string('b', 60) }))
        );
    }

    [Fact]
    public async Task PatchRelay_RemoteIdThatFitsTheSavedCircuitId_IsAccepted()
    {
        // The startup circuit ID is 200 characters, the saved one is short: 10 + 60 fits only
        // against the saved value.
        string path = Path.Combine(Path.GetTempPath(), $"ninlil-relay-{Guid.NewGuid():N}.json");

        try
        {
            DhcpSettingsController controller = CreateRelayController(
                startup: new Dhcpv4RelayOptions { RelayAgentCircuitId = new string('a', 200) },
                live: new Dhcpv4RelayOptions { RelayAgentCircuitId = "circuit-10" },
                configurationFilePath: path
            );

            ActionResult<RuntimeSettingsWriteResult> result = await controller.PatchRelay(
                ToJson(new { relayAgentRemoteId = new string('b', 60) })
            );

            Assert.IsType<OkObjectResult>(result.Result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PatchRaProtocol_MinimumThatBreaksTheRatioToTheSavedMaximum_IsRefused()
    {
        // Saved maximum 300 s: a minimum of 250 s is above 0.75 * 300 s. Against the startup
        // maximum of 600 s it would pass.
        DhcpSettingsController controller = CreateRaProtocolController(
            new RaProtocolOptions
            {
                MinRouterAdvertisementInterval = TimeSpan.FromSeconds(100),
                MaxRouterAdvertisementInterval = TimeSpan.FromSeconds(300),
            }
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.PatchRaProtocol(ToJson(new { minRouterAdvertisementInterval = "00:04:10" }))
        );
    }

    [Fact]
    public async Task PatchRaProtocol_MinimumThatFitsTheSavedMaximum_IsAccepted()
    {
        // Saved maximum 1200 s: a minimum of 600 s is within 0.75 * 1200 s. Against the startup
        // maximum of 600 s it would break the ratio.
        string path = Path.Combine(Path.GetTempPath(), $"ninlil-ra-{Guid.NewGuid():N}.json");

        try
        {
            DhcpSettingsController controller = CreateRaProtocolController(
                new RaProtocolOptions
                {
                    MinRouterAdvertisementInterval = TimeSpan.FromSeconds(300),
                    MaxRouterAdvertisementInterval = TimeSpan.FromSeconds(1200),
                },
                path
            );

            ActionResult<RuntimeSettingsWriteResult> result = await controller.PatchRaProtocol(
                ToJson(new { minRouterAdvertisementInterval = "00:10:00" })
            );

            Assert.IsType<OkObjectResult>(result.Result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    ///     This method serializes a patch body to the <see cref="JsonElement" /> a PATCH action
    ///     receives.
    /// </summary>
    private static JsonElement ToJson(object body) => JsonSerializer.SerializeToElement(body);

    /// <summary>
    ///     This method builds the controller with the given DHCPv4 relay options as the startup
    ///     value and as the live value. A patch that passes validation merges into
    ///     <paramref name="configurationFilePath" />.
    /// </summary>
    private static DhcpSettingsController CreateRelayController(
        Dhcpv4RelayOptions startup,
        Dhcpv4RelayOptions live,
        string configurationFilePath = null
    ) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new DhcpServerOptions()),
            new FixedMonitor<DhcpServerOptions>(new DhcpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(startup),
            new FixedMonitor<Dhcpv4RelayOptions>(live),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new TftpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new RaProtocolOptions()),
            new FixedMonitor<RaProtocolOptions>(new RaProtocolOptions()),
            new FixedMonitor<RaOptions>(new RaOptions()),
            new RuntimeConfigurationOptions { ConfigurationFilePath = configurationFilePath }
        );

    /// <summary>
    ///     This method builds the controller with default Router Advertisement transport options
    ///     as the startup value and the given options as the live value.
    /// </summary>
    private static DhcpSettingsController CreateRaProtocolController(
        RaProtocolOptions live,
        string configurationFilePath = null
    ) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new DhcpServerOptions()),
            new FixedMonitor<DhcpServerOptions>(new DhcpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4RelayOptions()),
            new FixedMonitor<Dhcpv4RelayOptions>(new Dhcpv4RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new TftpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new RaProtocolOptions()),
            new FixedMonitor<RaProtocolOptions>(live),
            new FixedMonitor<RaOptions>(new RaOptions()),
            new RuntimeConfigurationOptions { ConfigurationFilePath = configurationFilePath }
        );

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
            new FixedMonitor<Dhcpv4RelayOptions>(new Dhcpv4RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new TftpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new RaProtocolOptions()),
            new FixedMonitor<RaProtocolOptions>(new RaProtocolOptions()),
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

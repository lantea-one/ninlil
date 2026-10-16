using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers <see cref="Options.Dhcpv4OptionOverlayService" />: scope custom
///     options (including overriding a standard option and the server-address value source) and
///     tag rules on each criterion (Vendor Class Identifier, User Class, Agent Circuit ID and
///     Remote ID), scope eligibility and priority order. Each test calls
///     <see cref="Dhcpv4RequestHandler.HandleRequestAsync" /> on a <see cref="Dhcpv4EngineTestHost" />
///     with a temporary SQLite file.
/// </summary>
public sealed class Dhcpv4OptionOverlayServiceTests : IAsyncLifetime
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
    public async Task Discover_WithCustomOption_DeliversItAlongsideStandardOptions()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "custom-option-scope",
            "192.0.2.50",
            "192.0.2.52",
            gatewayAddresses: ["192.0.2.1"]
        );

        // Tag 66 (TFTP Server Name) gets no standard value here, since the scope has no boot file.
        await _host.SeedCustomOptionAsync(scope.Id, 66, "746674702e6578616d706c65");

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x01, 0x01, 0x01, 0x01, 0x01, 0x01], 0x5000),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(66, out byte[] rawValue));
        Assert.Equal("tftp.example", System.Text.Encoding.ASCII.GetString(rawValue));

        // The scope's standard Router option must still be there: a custom option for another
        // tag adds to the standard options.
        Assert.True(offer.TryGetRouters(out IReadOnlyList<IPAddress> routers));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(routers));
    }

    [Fact]
    public async Task Discover_WithCustomOptionForStandardTag_OverridesTheStandardValue()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "custom-override-scope",
            "192.0.2.60",
            "192.0.2.62",
            subnetPrefixLength: 24
        );

        // Tag 1 is Subnet Mask, which SubnetPrefixLength would give as 255.255.255.0; the custom
        // value must win (see DhcpScopeCustomOptionEntity).
        await _host.SeedCustomOptionAsync(scope.Id, 1, "fffffe00");

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x02, 0x02, 0x02, 0x02, 0x02, 0x02], 0x5001),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetSubnetMask(out IPAddress subnetMask));
        Assert.Equal(IPAddress.Parse("255.255.254.0"), subnetMask);
    }

    [Fact]
    public async Task Discover_WithServerAddressCustomOption_DeliversThisServersOwnAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "server-address-custom-option-scope",
            "192.0.2.170",
            "192.0.2.172"
        );

        // Tag 6 (Domain Name Server) is server-address eligible. ValueHex is blank, as it is when
        // an operator picks ServerAddress (see DhcpScopeCustomOptionEntity.ValueHex).
        await _host.SeedCustomOptionAsync(
            scope.Id,
            6,
            valueHex: string.Empty,
            valueSource: DhcpOptionValueSourceEnum.ServerAddress
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x0d, 0x0d, 0x0d, 0x0d, 0x0d, 0x0d], 0x500c),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(6, out byte[] rawValue));
        Assert.Equal(
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress).GetAddressBytes(),
            rawValue
        );
    }

    [Fact]
    public async Task Discover_WithServerAddressTagRuleOption_DeliversThisServersOwnAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "server-address-tag-rule-scope",
            "192.0.2.180",
            "192.0.2.182"
        );

        // Tag 42 (Network Time Protocol Servers) is also server-address eligible. Using a tag rule
        // here shows the substitution works for rule options as well as custom options.
        await _host.SeedTagRuleAsync(
            "NTP via this server",
            new Dictionary<byte, string> { [42] = string.Empty },
            scopeId: scope.Id,
            serverAddressOptionTags: new HashSet<byte> { 42 }
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x0e, 0x0e, 0x0e, 0x0e, 0x0e, 0x0e], 0x500d),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(42, out byte[] rawValue));
        Assert.Equal(
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress).GetAddressBytes(),
            rawValue
        );
    }

    [Fact]
    public async Task Discover_WithDisabledCustomOption_IsNotDelivered()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "disabled-custom-option-scope",
            "192.0.2.70",
            "192.0.2.72"
        );

        await _host.SeedCustomOptionAsync(
            scope.Id,
            66,
            "746674702e6578616d706c65",
            isDisabled: true
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x03, 0x03, 0x03, 0x03, 0x03, 0x03], 0x5002),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.False(offer.TryGetRawOption(66, out _));
    }

    [Fact]
    public async Task Discover_MatchingVendorClassRule_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "vendor-class-scope",
            "192.0.2.80",
            "192.0.2.82"
        );

        await _host.SeedTagRuleAsync(
            "PXE clients",
            new Dictionary<byte, string> { [67] = "70786574" },
            scopeId: scope.Id,
            vendorClassIdentifierMatch: "PXEClient",
            vendorClassIdentifierMatchMode: Dhcpv4TagMatchModeEnum.Prefix
        );

        Dhcpv4Message discover = NewDiscover([0x04, 0x04, 0x04, 0x04, 0x04, 0x04], 0x5003);
        discover.SetVendorClassIdentifier("PXEClient:Arch:00000");

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(67, out _));
    }

    [Fact]
    public async Task Discover_NonMatchingVendorClassRule_DoesNotApplyRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "vendor-class-nomatch-scope",
            "192.0.2.90",
            "192.0.2.92"
        );

        await _host.SeedTagRuleAsync(
            "PXE clients",
            new Dictionary<byte, string> { [67] = "70786574" },
            scopeId: scope.Id,
            vendorClassIdentifierMatch: "PXEClient",
            vendorClassIdentifierMatchMode: Dhcpv4TagMatchModeEnum.Prefix
        );

        Dhcpv4Message discover = NewDiscover([0x05, 0x05, 0x05, 0x05, 0x05, 0x05], 0x5004);
        discover.SetVendorClassIdentifier("MSFT 5.0");

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.False(offer.TryGetRawOption(67, out _));
    }

    [Fact]
    public async Task Discover_MatchingUserClassRule_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "user-class-scope",
            "192.0.2.100",
            "192.0.2.102"
        );

        await _host.SeedTagRuleAsync(
            "Guest devices",
            new Dictionary<byte, string> { [43] = "01" },
            scopeId: scope.Id,
            userClassMatch: "guest"
        );

        Dhcpv4Message discover = NewDiscover([0x06, 0x06, 0x06, 0x06, 0x06, 0x06], 0x5005);
        discover.SetUserClasses(["guest"]);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(43, out _));
    }

    [Fact]
    public async Task Discover_RelayedWithMatchingCircuitId_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "circuit-id-scope",
            "192.0.2.110",
            "192.0.2.112"
        );

        byte[] circuitId = [0x00, 0x01, 0x02, 0x03];
        await _host.SeedTagRuleAsync(
            "Wiring closet A",
            new Dictionary<byte, string> { [43] = "02" },
            scopeId: scope.Id,
            relayAgentCircuitIdHex: Convert.ToHexString(circuitId)
        );

        Dhcpv4Message discover = NewDiscover([0x07, 0x07, 0x07, 0x07, 0x07, 0x07], 0x5006);
        discover.SetRelayAgentInformation(circuitId: circuitId);
        discover.RelayAgentIpAddress = IPAddress.Parse("192.0.2.254");

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(43, out _));
    }

    [Fact]
    public async Task Discover_NotRelayed_DoesNotMatchACircuitIdRule()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "no-relay-scope",
            "192.0.2.120",
            "192.0.2.122"
        );

        await _host.SeedTagRuleAsync(
            "Wiring closet A",
            new Dictionary<byte, string> { [43] = "02" },
            scopeId: scope.Id,
            relayAgentCircuitIdHex: "00010203"
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x08, 0x08, 0x08, 0x08, 0x08, 0x08], 0x5007),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.False(offer.TryGetRawOption(43, out _));
    }

    [Fact]
    public async Task Discover_TwoMatchingRulesSameTag_HigherPriorityWins()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "priority-scope",
            "192.0.2.130",
            "192.0.2.132"
        );

        // Both rules match every client of the scope (no criteria besides ScopeId). The rule
        // with the higher Priority is applied last, so its value for tag 60 must win.
        await _host.SeedTagRuleAsync(
            "Low priority",
            new Dictionary<byte, string> { [200] = "01" },
            scopeId: scope.Id,
            priority: 1
        );
        await _host.SeedTagRuleAsync(
            "High priority",
            new Dictionary<byte, string> { [200] = "02" },
            scopeId: scope.Id,
            priority: 10
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x09, 0x09, 0x09, 0x09, 0x09, 0x09], 0x5008),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(200, out byte[] value));
        Assert.Equal(0x02, Assert.Single(value));
    }

    [Fact]
    public async Task Discover_GlobalRuleWithNoScopeId_MatchesClientInAnyScope()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "global-rule-scope",
            "192.0.2.140",
            "192.0.2.142"
        );

        // No scopeId: eligible in every scope.
        await _host.SeedTagRuleAsync(
            "Everyone, everywhere",
            new Dictionary<byte, string> { [201] = "01" }
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x0a, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a], 0x5009),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(201, out _));
        Assert.NotEqual(Guid.Empty, scope.Id);
    }

    [Fact]
    public async Task Discover_DisabledRule_NeverMatches()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "disabled-rule-scope",
            "192.0.2.150",
            "192.0.2.152"
        );

        await _host.SeedTagRuleAsync(
            "Disabled rule",
            new Dictionary<byte, string> { [202] = "01" },
            scopeId: scope.Id,
            isDisabled: true
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x0b, 0x0b, 0x0b, 0x0b, 0x0b, 0x0b], 0x500a),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.False(offer.TryGetRawOption(202, out _));
    }

    [Fact]
    public async Task Discover_WithNoCustomOptionsOrRules_ReceivesOnlyBaseStandardOptions()
    {
        await _host.SeedScopeAsync(
            "plain-scope",
            "192.0.2.160",
            "192.0.2.162",
            gatewayAddresses: ["192.0.2.1"]
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c], 0x500b),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRouters(out IReadOnlyList<IPAddress> routers));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(routers));
        Assert.False(offer.TryGetRawOption(200, out _));
        Assert.False(offer.TryGetRawOption(201, out _));
        Assert.False(offer.TryGetRawOption(202, out _));
    }

    [Fact]
    public async Task Discover_WithStoredOptionsBreakingTheRules_SkipsThemAndStillEncodes()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "bad-stored-options-scope",
            "192.0.2.190",
            "192.0.2.192"
        );

        // Rows like these can exist from before the REST layer enforced the rules.
        await _host.SeedCustomOptionAsync(scope.Id, 0, "AABB");
        await _host.SeedCustomOptionAsync(scope.Id, 53, "05");
        await _host.SeedCustomOptionAsync(scope.Id, 200, new string('A', 600));
        await _host.SeedCustomOptionAsync(scope.Id, 66, "746674702e6578616d706c65");
        await _host.SeedTagRuleAsync(
            "Oversized rule option",
            new Dictionary<byte, string> { [201] = new string('B', 600), [202] = "0102" },
            scopeId: scope.Id
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x0f, 0x0f, 0x0f, 0x0f, 0x0f, 0x0f], 0x500e),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Offer, messageType);
        Assert.False(offer.TryGetRawOption(0, out _));
        Assert.False(offer.TryGetRawOption(200, out _));
        Assert.False(offer.TryGetRawOption(201, out _));
        Assert.True(offer.TryGetRawOption(66, out _));
        Assert.True(offer.TryGetRawOption(202, out _));
        Assert.NotEmpty(Dhcpv4MessageWriter.Write(offer));
    }

    [Fact]
    public async Task Discover_WithServerAddressOnTheTftpServerName_DeliversTheAddressAsText()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "server-address-tftp-name-scope",
            "192.0.2.200",
            "192.0.2.202"
        );

        // Tag 66 is a string option (RFC 2132 Section 9.4), so "this server's address" is its
        // dotted-quad text and not the 4 octets that tag 6 carries.
        await _host.SeedCustomOptionAsync(
            scope.Id,
            66,
            valueHex: string.Empty,
            valueSource: DhcpOptionValueSourceEnum.ServerAddress
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x10, 0x10, 0x10, 0x10, 0x10, 0x10], 0x500f),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(66, out byte[] rawValue));
        Assert.Equal(
            Dhcpv4EngineTestHost.ServerIdentifierAddress,
            System.Text.Encoding.ASCII.GetString(rawValue)
        );
    }

    [Fact]
    public async Task Discover_WithAStoredBinaryTftpServerName_SkipsItWithOneWarning()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "binary-tftp-name-scope",
            "192.0.2.210",
            "192.0.2.212"
        );

        // C0000201 is 192.0.2.1 as 4 octets, which an older build allowed under tag 66. The value
        // is not printable ASCII, so the overlay must drop it and say so once.
        DhcpScopeCustomOptionEntity row = await _host.SeedCustomOptionAsync(
            scope.Id,
            66,
            "C0000201"
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover([0x11, 0x11, 0x11, 0x11, 0x11, 0x11], 0x5010),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.False(offer.TryGetRawOption(66, out _));
        Assert.Single(_host.Logs.Warnings, warning => warning.Contains(row.Id.ToString()));
    }

    private static Dhcpv4Message NewDiscover(byte[] clientHardwareAddress, uint transactionId)
    {
        Dhcpv4Message discover = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        return discover;
    }
}

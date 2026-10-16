using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers <see cref="Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService" />
///     on each DHCPv6 criterion (vendor-class-data, enterprise number, user class, relayed
///     Interface-ID and Remote-ID), the server-address value source and priority order, and the
///     scope custom options a DHCPv6 scope delivers (code 32, code 23 replacing the DNS servers,
///     disabled rows, tag rule precedence, a stored reserved code) and the DNS servers of the
///     wrong address family. Global rules are DHCPv4 rules and never reach a DHCPv6 reply, and a
///     rule with a DHCPv4-only criterion never matches. Each
///     test calls <see cref="Dhcpv6RequestHandler.HandleRequestAsync" /> or
///     <see cref="Dhcpv6RequestHandler.HandleRelayMessageAsync" /> on a
///     <see cref="Dhcpv6EngineTestHost" />, like <see cref="Dhcpv4OptionOverlayServiceTests" />.
/// </summary>
public sealed class Dhcpv6OptionOverlayServiceTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.IPv6Loopback, 547);

    private Dhcpv6EngineTestHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv6EngineTestHost();
        await _host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Solicit_MatchingVendorClassDataRule_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "vendor-class-scope",
            "2001:db8:1::10",
            "2001:db8:1::12"
        );

        await _host.SeedTagRuleAsync(
            "PXE clients",
            new Dictionary<byte, string> { [67] = "70786574" },
            scopeId: scope.Id,
            vendorClassIdentifierMatch: "PXEClient",
            vendorClassIdentifierMatchMode: Dhcpv4TagMatchModeEnum.Prefix
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.SetVendorClass(32473, ["PXEClient:Arch:00000"]);

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(67, out _));
    }

    [Fact]
    public async Task Solicit_NonMatchingVendorClassDataRule_DoesNotApplyRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "vendor-class-nomatch-scope",
            "2001:db8:2::10",
            "2001:db8:2::12"
        );

        await _host.SeedTagRuleAsync(
            "PXE clients",
            new Dictionary<byte, string> { [67] = "70786574" },
            scopeId: scope.Id,
            vendorClassIdentifierMatch: "PXEClient",
            vendorClassIdentifierMatchMode: Dhcpv4TagMatchModeEnum.Prefix
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x02, 0x02, 0x02, 0x02, 0x00, 0x02];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1001);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.SetVendorClass(32473, ["SomeOtherClient"]);

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(67, out _));
    }

    [Fact]
    public async Task Solicit_MatchingVendorClassEnterpriseNumberOnlyRule_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "vendor-enterprise-scope",
            "2001:db8:3::10",
            "2001:db8:3::12"
        );

        // No vendor-class-data text: the enterprise number alone means "any vendor class from
        // enterprise 32473" (see DhcpTagRuleEntity.VendorClassEnterpriseNumber).
        await _host.SeedTagRuleAsync(
            "Enterprise-only match",
            new Dictionary<byte, string> { [67] = "70786574" },
            scopeId: scope.Id,
            vendorClassEnterpriseNumber: 32473
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x03, 0x03, 0x03, 0x03, 0x00, 0x03];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1002);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.SetVendorClass(32473, ["AnythingAtAll"]);

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(67, out _));
    }

    [Fact]
    public async Task Solicit_WrongVendorClassEnterpriseNumber_DoesNotApplyRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "vendor-enterprise-nomatch-scope",
            "2001:db8:4::10",
            "2001:db8:4::12"
        );

        await _host.SeedTagRuleAsync(
            "Enterprise-only match",
            new Dictionary<byte, string> { [67] = "70786574" },
            scopeId: scope.Id,
            vendorClassEnterpriseNumber: 32473
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1003);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.SetVendorClass(9999, ["AnythingAtAll"]);

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(67, out _));
    }

    [Fact]
    public async Task Solicit_WithServerAddressTagRuleOption_DeliversThisServersOwnAddress()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "server-address-tag-rule-scope",
            "2001:db8:c::10",
            "2001:db8:c::12"
        );

        // Code 23 (DNS Recursive Name Server) is server-address eligible. The scope has no
        // DnsServerAddresses, so option 23 can only come from the substitution.
        await _host.SeedTagRuleAsync(
            "DNS via this server",
            new Dictionary<byte, string> { [23] = string.Empty },
            scopeId: scope.Id,
            serverAddressOptionTags: new HashSet<byte> { 23 }
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0c, 0x0c, 0x0c, 0x0c, 0x00, 0x0c];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x100b);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(23, out byte[] rawValue));
        Assert.Equal(
            IPAddress.Parse(Dhcpv6EngineTestHost.ServerAddress).GetAddressBytes(),
            rawValue
        );
    }

    [Fact]
    public async Task Solicit_MatchingUserClassRule_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "user-class-scope",
            "2001:db8:5::10",
            "2001:db8:5::12"
        );

        await _host.SeedTagRuleAsync(
            "Guest devices",
            new Dictionary<byte, string> { [43] = "01" },
            scopeId: scope.Id,
            userClassMatch: "guest"
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x05, 0x05, 0x05, 0x05, 0x00, 0x05];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1004);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.SetUserClasses(["guest"]);

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(43, out _));
    }

    [Fact]
    public async Task Solicit_RelayedWithMatchingInterfaceId_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "interface-id-scope",
            "2001:db8:6::10",
            "2001:db8:6::12"
        );

        byte[] interfaceId = [0x65, 0x74, 0x68, 0x30]; // "eth0"
        await _host.SeedTagRuleAsync(
            "Wiring closet A",
            new Dictionary<byte, string> { [200] = "02" },
            scopeId: scope.Id,
            relayInterfaceIdHex: Convert.ToHexString(interfaceId)
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x06, 0x06, 0x06, 0x06, 0x00, 0x06];
        Dhcpv6Message innerSolicit = NewSolicit(clientDuid, 0x1005);
        innerSolicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6RelayMessage relayForward = NewRelayForward(
            innerSolicit,
            IPAddress.Parse("2001:db8:6::1")
        );
        relayForward.SetInterfaceId(interfaceId);

        Dhcpv6RelayMessage relayReply = await _host.RelayHandler.HandleRelayMessageAsync(
            relayForward,
            RemoteEndPoint,
            CancellationToken.None
        );

        Dhcpv6Message advertise = UnwrapInnerReply(relayReply);
        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(200, out _));
    }

    [Fact]
    public async Task Solicit_RelayedWithNonMatchingInterfaceId_DoesNotApplyRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "interface-id-nomatch-scope",
            "2001:db8:7::10",
            "2001:db8:7::12"
        );

        await _host.SeedTagRuleAsync(
            "Wiring closet A",
            new Dictionary<byte, string> { [200] = "02" },
            scopeId: scope.Id,
            relayInterfaceIdHex: "65746830"
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x07, 0x07, 0x07, 0x07, 0x00, 0x07];
        Dhcpv6Message innerSolicit = NewSolicit(clientDuid, 0x1006);
        innerSolicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6RelayMessage relayForward = NewRelayForward(
            innerSolicit,
            IPAddress.Parse("2001:db8:7::1")
        );
        relayForward.SetInterfaceId([0x65, 0x74, 0x68, 0x31]); // "eth1", which does not match

        Dhcpv6RelayMessage relayReply = await _host.RelayHandler.HandleRelayMessageAsync(
            relayForward,
            RemoteEndPoint,
            CancellationToken.None
        );

        Dhcpv6Message advertise = UnwrapInnerReply(relayReply);
        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(200, out _));
    }

    [Fact]
    public async Task Solicit_NotRelayed_DoesNotMatchAnInterfaceIdRule()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "no-relay-scope",
            "2001:db8:8::10",
            "2001:db8:8::12"
        );

        await _host.SeedTagRuleAsync(
            "Wiring closet A",
            new Dictionary<byte, string> { [200] = "02" },
            scopeId: scope.Id,
            relayInterfaceIdHex: "65746830"
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x08, 0x08, 0x08, 0x08, 0x00, 0x08];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1007);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(200, out _));
    }

    [Fact]
    public async Task Solicit_RelayedWithMatchingRemoteId_AppliesRuleOptions()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "remote-id-scope",
            "2001:db8:9::10",
            "2001:db8:9::12"
        );

        // RFC 4649 format: a 4-octet enterprise number followed by the remote-id value.
        byte[] remoteId = [0x00, 0x00, 0x7E, 0xD9, 0xAA, 0xBB];
        await _host.SeedTagRuleAsync(
            "Remote-ID match",
            new Dictionary<byte, string> { [201] = "03" },
            scopeId: scope.Id,
            relayRemoteIdHex: Convert.ToHexString(remoteId)
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x09, 0x09, 0x09, 0x09, 0x00, 0x09];
        Dhcpv6Message innerSolicit = NewSolicit(clientDuid, 0x1008);
        innerSolicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6RelayMessage relayForward = NewRelayForward(
            innerSolicit,
            IPAddress.Parse("2001:db8:9::1")
        );
        relayForward.SetRemoteId(remoteId);

        Dhcpv6RelayMessage relayReply = await _host.RelayHandler.HandleRelayMessageAsync(
            relayForward,
            RemoteEndPoint,
            CancellationToken.None
        );

        Dhcpv6Message advertise = UnwrapInnerReply(relayReply);
        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(201, out _));
    }

    [Fact]
    public async Task Solicit_TwoMatchingScopeRulesSameTag_HigherPriorityWins()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "priority-scope",
            "2001:db8:a::10",
            "2001:db8:a::12"
        );

        // Both rules have no criteria, so they match every client of the scope.
        await _host.SeedTagRuleAsync(
            "Low priority",
            new Dictionary<byte, string> { [210] = "01" },
            scopeId: scope.Id,
            priority: 1
        );
        await _host.SeedTagRuleAsync(
            "High priority",
            new Dictionary<byte, string> { [210] = "02" },
            scopeId: scope.Id,
            priority: 10
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0a, 0x0a, 0x0a, 0x0a, 0x00, 0x0a];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1009);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(210, out byte[] value));
        Assert.Equal(0x02, Assert.Single(value));
    }

    [Fact]
    public async Task Solicit_GlobalRuleWithNoCriteria_IsNotAppliedToADhcpv6Reply()
    {
        await _host.SeedAddressScopeAsync(
            "global-rule-scope",
            "2001:db8:d1::10",
            "2001:db8:d1::12"
        );

        // A global rule is a DHCPv4 rule. Its tag would be written as a DHCPv6 option code of
        // the same number, which means something else.
        await _host.SeedTagRuleAsync(
            "Global DHCPv4 rule",
            new Dictionary<byte, string> { [214] = "01" }
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0d, 0x0d, 0x0d, 0x0d, 0x00, 0x0d];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1d01);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(214, out _));
    }

    [Fact]
    public async Task Solicit_ScopeRuleWithNoCriteria_IsApplied()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "scope-rule-scope",
            "2001:db8:d2::10",
            "2001:db8:d2::12"
        );

        await _host.SeedTagRuleAsync(
            "Scope rule",
            new Dictionary<byte, string> { [212] = "01" },
            scopeId: scope.Id
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0e, 0x0e, 0x0e, 0x0e, 0x00, 0x0e];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1d02);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(212, out _));
    }

    [Fact]
    public async Task Solicit_ScopeRuleWithOnlyACircuitId_NeverMatches()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "circuit-rule-scope",
            "2001:db8:d3::10",
            "2001:db8:d3::12"
        );

        // The Circuit ID is a DHCPv4 criterion. A DHCPv6 engine cannot check it, so the rule
        // must not widen to every client.
        await _host.SeedTagRuleAsync(
            "Circuit rule",
            new Dictionary<byte, string> { [213] = "01" },
            scopeId: scope.Id,
            relayAgentCircuitIdHex: "0102"
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0f, 0x0f, 0x0f, 0x0f, 0x00, 0x0f];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1d03);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(213, out _));
    }

    [Fact]
    public async Task Solicit_DisabledRule_NeverMatches()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "disabled-rule-scope",
            "2001:db8:b::10",
            "2001:db8:b::12"
        );

        await _host.SeedTagRuleAsync(
            "Disabled rule",
            new Dictionary<byte, string> { [211] = "01" },
            scopeId: scope.Id,
            isDisabled: true
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0b, 0x0b, 0x0b, 0x0b, 0x00, 0x0b];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x100a);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(211, out _));
    }

    [Fact]
    public async Task Solicit_WithStoredRuleOptionsBreakingTheRules_SkipsThemAndStillEncodes()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "bad-stored-options-scope",
            "2001:db8:b6::10",
            "2001:db8:b6::12"
        );

        // Code 0 is reserved and 70 000 bytes do not fit the two-byte length field. The byte
        // tag type cannot hold a code over 255, so the oversized value uses tag 67.
        await _host.SeedTagRuleAsync(
            "Bad stored options",
            new Dictionary<byte, string>
            {
                [0] = "AABB",
                [67] = new string('A', 140_000),
                [68] = "70786574",
            },
            scopeId: scope.Id
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0b, 0x0b, 0x0b, 0x0b, 0x00, 0x0b];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1b60);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(0, out _));
        Assert.False(advertise.TryGetRawOption(67, out _));
        Assert.True(advertise.TryGetRawOption(68, out _));
        Assert.NotEmpty(Dhcpv6MessageWriter.Write(advertise));
    }

    [Fact]
    public async Task Solicit_WithTagRuleDomainSearchListHoldingText_SkipsItWithOneWarning()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "text-search-list-scope",
            "2001:db8:d::10",
            "2001:db8:d::12"
        );

        // "example.com" as ASCII text is what an older admin console stored for code 24. The
        // option is a list of wire-format names, so the overlay must drop it.
        DhcpTagRuleEntity rule = await _host.SeedTagRuleAsync(
            "Text search list",
            new Dictionary<byte, string> { [24] = "6578616D706C652E636F6D" },
            scopeId: scope.Id
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0d, 0x0d, 0x0d, 0x0d, 0x00, 0x0d];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1d60);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(24, out _));
        Assert.Single(_host.Logs.Warnings, warning => warning.Contains(rule.Id.ToString()));
    }

    [Fact]
    public async Task Solicit_WithTagRuleDomainSearchListInWireForm_DeliversIt()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "wire-search-list-scope",
            "2001:db8:e::10",
            "2001:db8:e::12"
        );

        // 07example03com00 is "example.com" in RFC 1035 Section 3.1 wire form.
        await _host.SeedTagRuleAsync(
            "Wire search list",
            new Dictionary<byte, string> { [24] = "076578616D706C6503636F6D00" },
            scopeId: scope.Id
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0e, 0x0e, 0x0e, 0x0e, 0x00, 0x0e];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1e60);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetDomainSearchList(out IReadOnlyList<string> domains));
        Assert.Equal("example.com", Assert.Single(domains));
    }

    [Fact]
    public async Task Solicit_WithTagRuleServerIdentifier_KeepsTheEnginesOwnServerIdentifier()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "server-identifier-rule-scope",
            "2001:db8:f::10",
            "2001:db8:f::12"
        );

        // Code 2 is the Server Identifier, which the engine writes itself. A rule option with that
        // code would replace it and every client would reject the reply.
        DhcpTagRuleEntity rule = await _host.SeedTagRuleAsync(
            "Server identifier override",
            new Dictionary<byte, string> { [2] = "00030001AABBCCDDEEFF" },
            scopeId: scope.Id
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x0f, 0x0f, 0x0f, 0x0f, 0x00, 0x0f];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1f60);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(2, out byte[] serverIdentifier));
        Assert.NotEqual(Convert.FromHexString("00030001AABBCCDDEEFF"), serverIdentifier);
        Assert.Single(_host.Logs.Warnings, warning => warning.Contains(rule.Id.ToString()));
    }

    [Fact]
    public async Task Solicit_WithScopeCustomOption_AdvertiseCarriesIt()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "custom-option-scope",
            "2001:db8:c1::10",
            "2001:db8:c1::12"
        );

        // Code 32 is Information Refresh Time (RFC 8415 Section 21.23); 4 octets, 3600 s.
        await _host.SeedCustomOptionAsync(scope.Id, 32, "00000E10");

        Dhcpv6Message advertise = await SolicitAsync(0x11, 0x2001);

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(32, out byte[] value));
        Assert.Equal(Convert.FromHexString("00000E10"), value);
    }

    [Fact]
    public async Task InformationRequest_WithScopeCustomOption_ReplyCarriesIt()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "custom-option-inform-scope",
            "2001:db8:c2::10",
            "2001:db8:c2::12"
        );
        await _host.SeedCustomOptionAsync(scope.Id, 32, "00000E10");

        Dhcpv6Message informationRequest = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.InformationRequest,
            TransactionId = 0x2002,
        };
        informationRequest.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x12, 0x12, 0x12, 0x12]);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            informationRequest,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        Assert.True(reply.TryGetRawOption(32, out byte[] value));
        Assert.Equal(Convert.FromHexString("00000E10"), value);
    }

    [Fact]
    public async Task Solicit_WithCustomOptionForDnsServers_ReplacesTheScopesDnsServers()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "custom-dns-scope",
            "2001:db8:c3::10",
            "2001:db8:c3::12",
            dnsServerAddresses: ["2001:db8:c3::1"]
        );

        // Code 23 (DNS Recursive Name Server) with one address.
        await _host.SeedCustomOptionAsync(
            scope.Id,
            23,
            Convert.ToHexString(IPAddress.Parse("2001:db8:c3::99").GetAddressBytes())
        );

        Dhcpv6Message advertise = await SolicitAsync(0x13, 0x2003);

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetDnsServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(IPAddress.Parse("2001:db8:c3::99"), Assert.Single(dnsServers));
    }

    [Fact]
    public async Task Solicit_WithDisabledCustomOption_DoesNotSendIt()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "custom-disabled-scope",
            "2001:db8:c4::10",
            "2001:db8:c4::12"
        );
        await _host.SeedCustomOptionAsync(scope.Id, 32, "00000E10", isDisabled: true);

        Dhcpv6Message advertise = await SolicitAsync(0x14, 0x2004);

        Assert.NotNull(advertise);
        Assert.False(advertise.TryGetRawOption(32, out _));
    }

    [Fact]
    public async Task Solicit_WithTagRuleAndCustomOptionForTheSameCode_TheTagRuleWins()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "custom-vs-rule-scope",
            "2001:db8:c5::10",
            "2001:db8:c5::12"
        );
        await _host.SeedCustomOptionAsync(scope.Id, 32, "00000E10");
        await _host.SeedTagRuleAsync(
            "Refresh time rule",
            new Dictionary<byte, string> { [32] = "00001C20" },
            scopeId: scope.Id
        );

        Dhcpv6Message advertise = await SolicitAsync(0x15, 0x2005);

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(32, out byte[] value));
        Assert.Equal(Convert.FromHexString("00001C20"), value);
    }

    [Fact]
    public async Task Solicit_WithStoredCustomOptionForTheServerIdentifier_KeepsTheEnginesOwn()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "custom-server-id-scope",
            "2001:db8:c6::10",
            "2001:db8:c6::12"
        );

        // The API refuses code 2; a stored row (older data) must not replace the Server Identifier.
        DhcpScopeCustomOptionEntity row = await _host.SeedCustomOptionAsync(
            scope.Id,
            2,
            "00030001AABBCCDDEEFF"
        );

        Dhcpv6Message advertise = await SolicitAsync(0x16, 0x2006);

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(2, out byte[] serverIdentifier));
        Assert.NotEqual(Convert.FromHexString("00030001AABBCCDDEEFF"), serverIdentifier);
        Assert.Single(_host.Logs.Warnings, warning => warning.Contains(row.Id.ToString()));
    }

    [Fact]
    public async Task Solicit_WithAnIPv4DnsServerStoredOnTheScope_StillRepliesWithTheIPv6Ones()
    {
        await _host.SeedAddressScopeAsync(
            "wrong-family-dns-scope",
            "2001:db8:c7::10",
            "2001:db8:c7::12",
            dnsServerAddresses: ["192.0.2.53", "2001:db8::53"]
        );

        Dhcpv6Message advertise = await SolicitAsync(0x17, 0x2007);

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetDnsServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(IPAddress.Parse("2001:db8::53"), Assert.Single(dnsServers));
    }

    /// <summary>
    ///     This method sends a SOLICIT for one IA_NA from a client whose DUID ends in
    ///     <paramref name="seed" /> and returns the ADVERTISE.
    /// </summary>
    private async Task<Dhcpv6Message> SolicitAsync(byte seed, uint transactionId)
    {
        Dhcpv6Message solicit = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, seed, seed, seed, seed, 0x00, seed],
            transactionId
        );
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        return await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
    }

    private static Dhcpv6Message NewSolicit(byte[] clientDuid, uint transactionId)
    {
        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = transactionId,
        };
        solicit.SetClientIdentifier(clientDuid);
        solicit.SetElapsedTime(TimeSpan.Zero);
        return solicit;
    }

    private static Dhcpv6IdentityAssociationForNonTemporaryAddresses NewNaRequest(
        uint identityAssociationId
    ) => new() { IdentityAssociationId = identityAssociationId };

    /// <summary>
    ///     This method wraps a client message in a RELAY-FORW, as an external relay agent would,
    ///     like <see cref="Dhcpv6RelayReceivingRoundTripTests" />.
    /// </summary>
    private static Dhcpv6RelayMessage NewRelayForward(Dhcpv6Message inner, IPAddress linkAddress)
    {
        Dhcpv6RelayMessage relayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            HopCount = 0,
            LinkAddress = linkAddress,
            PeerAddress = IPAddress.Parse("fe80::1"),
        };
        relayForward.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(inner));
        return relayForward;
    }

    /// <summary>
    ///     This method decodes the reply inside a RELAY-REPL.
    /// </summary>
    private static Dhcpv6Message UnwrapInnerReply(Dhcpv6RelayMessage relayReply)
    {
        Assert.NotNull(relayReply);
        Assert.True(relayReply.TryGetRelayMessagePayload(out byte[] innerReplyPayload));
        Assert.True(
            Dhcpv6MessageReader.TryRead(
                innerReplyPayload,
                out Dhcpv6Message innerReply,
                out string failureReason
            ),
            failureReason
        );
        return innerReply;
    }
}

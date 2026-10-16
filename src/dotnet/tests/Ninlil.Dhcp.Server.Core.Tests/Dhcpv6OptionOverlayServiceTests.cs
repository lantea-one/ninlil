using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers <see cref="Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService" />
///     on each DHCPv6 criterion (vendor-class-data, enterprise number, user class, relayed
///     Interface-ID and Remote-ID), the server-address value source and priority order. Each
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
    public async Task Solicit_TwoMatchingGlobalRulesSameTag_HigherPriorityWins()
    {
        await _host.SeedAddressScopeAsync("priority-scope", "2001:db8:a::10", "2001:db8:a::12");

        // Both rules have no criteria, so they match every eligible client.
        await _host.SeedTagRuleAsync(
            "Low priority",
            new Dictionary<byte, string> { [210] = "01" },
            priority: 1
        );
        await _host.SeedTagRuleAsync(
            "High priority",
            new Dictionary<byte, string> { [210] = "02" },
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

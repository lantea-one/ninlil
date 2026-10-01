using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the DHCPv6 engine's message handling (SOLICIT with and without
///     Rapid Commit, REQUEST, RENEW, REBIND, RELEASE, DECLINE, CONFIRM, INFORMATION-REQUEST,
///     prefix delegation and Client FQDN) by calling
///     <see cref="Dhcpv6RequestHandler.HandleRequestAsync" /> directly, without a socket (see
///     <see cref="Dhcpv6UdpRoundTripTests" />), on a <see cref="Dhcpv6EngineTestHost" /> with a
///     temporary SQLite file. It is the counterpart of <see cref="Dhcpv4RequestHandlerTests" />.
/// </summary>
public sealed class Dhcpv6RequestHandlerTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.IPv6Loopback, 546);

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
    public async Task Solicit_WithFreeAddressPool_AdvertisesFirstAvailableAddressAndStandardOptions()
    {
        await _host.SeedAddressScopeAsync(
            "solicit-na",
            "2001:db8:1::10",
            "2001:db8:1::12",
            dnsServerAddresses: ["2001:db8:1::1", "2001:db8:1::2"]
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x11, 0x11, 0x11, 0x11, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x1000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        Assert.Equal(solicit.TransactionId, advertise.TransactionId);
        Assert.True(advertise.TryGetServerIdentifier(out byte[] _));
        Assert.True(advertise.TryGetClientIdentifier(out byte[] echoedDuid));
        Assert.Equal(clientDuid, echoedDuid);
        Assert.True(advertise.TryGetDnsServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(2, dnsServers.Count);

        Assert.True(
            advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
            )
        );
        Dhcpv6IdentityAssociationForNonTemporaryAddresses association = Assert.Single(associations);
        Assert.Equal(1u, association.IdentityAssociationId);
        Assert.Equal(
            IPAddress.Parse("2001:db8:1::10"),
            Assert.Single(association.Addresses).Address
        );

        LeaseEntity lease = await _host.FindLeaseAsync("solicit-na", FormatDuid(clientDuid));
        Assert.NotNull(lease);
        Assert.Equal(LeaseStateEnum.Offered, lease.State);
    }

    [Fact]
    public async Task Solicit_WhenPoolExhausted_ReturnsNoAdvertise()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "solicit-full",
            "2001:db8:2::10",
            "2001:db8:2::10"
        );

        await ConfirmAddressForNewClientAsync(
            scope,
            [0x00, 0x01, 0x00, 0x01, 0x22, 0x22, 0x22, 0x22, 0x00, 0x01]
        );

        Dhcpv6Message solicit = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x33, 0x33, 0x33, 0x33, 0x00, 0x02],
            0x2000
        );
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(advertise);
    }

    [Fact]
    public async Task Solicit_ForClientWithReservation_AlwaysOffersReservedAddress()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "solicit-reserved",
            "2001:db8:3::10",
            "2001:db8:3::12"
        );
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x44, 0x44, 0x44, 0x44, 0x00, 0x01];
        await _host.SeedReservationAsync(scope.Id, FormatDuid(clientDuid), "2001:db8:3::99");

        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x3000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
        );
        Assert.Equal(
            IPAddress.Parse("2001:db8:3::99"),
            Assert.Single(Assert.Single(associations).Addresses).Address
        );
    }

    [Fact]
    public async Task Solicit_WithRapidCommitAllowed_CommitsDirectlyWithAReply()
    {
        await using Dhcpv6EngineTestHost rapidHost = new();
        await rapidHost.InitializeAsync(enableRapidCommit: true);
        await rapidHost.SeedAddressScopeAsync("solicit-rapid", "2001:db8:4::10", "2001:db8:4::12");

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x55, 0x55, 0x55, 0x55, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x4000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.SetRapidCommit();

        Dhcpv6Message reply = await rapidHost.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        Assert.True(reply.TryGetRapidCommit());

        LeaseEntity lease = await rapidHost.FindLeaseAsync("solicit-rapid", FormatDuid(clientDuid));
        Assert.NotNull(lease);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
        Assert.NotNull(lease.LeaseObtainedAtUtc);
    }

    [Fact]
    public async Task Solicit_ThenRequest_ConfirmsActiveLeaseAgainstTheSameServer()
    {
        await _host.SeedAddressScopeAsync("request-na", "2001:db8:5::10", "2001:db8:5::12");

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x66, 0x66, 0x66, 0x66, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x5000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
        advertise.TryGetServerIdentifier(out byte[] serverDuid);
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offeredAssociations
        );
        IPAddress offeredAddress = offeredAssociations[0].Addresses[0].Address;

        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, clientDuid, 0x5000);
        request.SetServerIdentifier(serverDuid);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestNa = NewNaRequest(1);
        requestNa.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = offeredAddress });
        request.AddIdentityAssociationForNonTemporaryAddresses(requestNa);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> repliedAssociations
        );
        Assert.Equal(offeredAddress, Assert.Single(repliedAssociations).Addresses[0].Address);

        LeaseEntity lease = await _host.FindLeaseAsync("request-na", FormatDuid(clientDuid));
        Assert.NotNull(lease);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
        Assert.NotNull(lease.LeaseObtainedAtUtc);
    }

    [Fact]
    public async Task Request_ForADifferentServerIdentifier_ReturnsNoReply()
    {
        await _host.SeedAddressScopeAsync(
            "request-wrong-server",
            "2001:db8:6::10",
            "2001:db8:6::12"
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x77, 0x77, 0x77, 0x77, 0x00, 0x01];
        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, clientDuid, 0x6000);
        request.SetServerIdentifier([0x00, 0x01, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x00, 0x99]);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestNa = NewNaRequest(1);
        requestNa.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse("2001:db8:6::10") }
        );
        request.AddIdentityAssociationForNonTemporaryAddresses(requestNa);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task Renew_ExtendsTheAlreadyActiveLease()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "renew-scope",
            "2001:db8:7::10",
            "2001:db8:7::12"
        );
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x88, 0x88, 0x88, 0x88, 0x00, 0x01];
        LeaseEntity original = await ConfirmAddressForNewClientAsync(scope, clientDuid);
        DateTimeOffset originalExpiry = original.LeaseExpiresAtUtc!.Value;

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Dhcpv6Message renew = NewMessage(Dhcpv6MessageTypeEnum.Renew, clientDuid, 0x7001);
        renew.SetServerIdentifier(await GetServerDuidAsync());
        Dhcpv6IdentityAssociationForNonTemporaryAddresses renewNa = NewNaRequest(1);
        renewNa.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse(original.IpAddress) }
        );
        renew.AddIdentityAssociationForNonTemporaryAddresses(renewNa);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            renew,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);

        LeaseEntity renewed = await _host.FindLeaseAsync("renew-scope", FormatDuid(clientDuid));
        Assert.Equal(LeaseStateEnum.Active, renewed.State);
        Assert.True(renewed.LeaseExpiresAtUtc >= originalExpiry);
    }

    [Fact]
    public async Task Rebind_SucceedsWithNoServerIdentifierAtAll()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "rebind-scope",
            "2001:db8:8::10",
            "2001:db8:8::12"
        );
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x99, 0x99, 0x99, 0x99, 0x00, 0x01];
        LeaseEntity original = await ConfirmAddressForNewClientAsync(scope, clientDuid);

        Dhcpv6Message rebind = NewMessage(Dhcpv6MessageTypeEnum.Rebind, clientDuid, 0x8001);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses rebindNa = NewNaRequest(1);
        rebindNa.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse(original.IpAddress) }
        );
        rebind.AddIdentityAssociationForNonTemporaryAddresses(rebindNa);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            rebind,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
        );
        Assert.False(
            associations[0].Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum _, out string _)
        );
    }

    [Fact]
    public async Task Release_FreesTheAddressImmediatelyAndAlwaysReplies()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "release-scope",
            "2001:db8:9::10",
            "2001:db8:9::10"
        );
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0xaa, 0xaa, 0xaa, 0xaa, 0x00, 0x01];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, clientDuid);

        Dhcpv6Message release = NewMessage(Dhcpv6MessageTypeEnum.Release, clientDuid, 0x9001);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses releaseNa = NewNaRequest(1);
        releaseNa.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse(active.IpAddress) }
        );
        release.AddIdentityAssociationForNonTemporaryAddresses(releaseNa);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            release,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply); // Unlike DHCPv4, a DHCPv6 server replies to a RELEASE.
        reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
        );
        Assert.True(
            associations[0]
                .Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum statusCode, out string _)
        );
        Assert.Equal(Dhcpv6StatusCodeEnum.Success, statusCode);

        // The one-address pool is free again for a new client.
        Dhcpv6Message solicit = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0xbb, 0xbb, 0xbb, 0xbb, 0x00, 0x02],
            0x9002
        );
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(advertise);
    }

    [Fact]
    public async Task Decline_QuarantinesTheAddressUntilTheWindowElapses()
    {
        await _host.SeedAddressScopeAsync("decline-scope", "2001:db8:a::10", "2001:db8:a::10");

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0xcc, 0xcc, 0xcc, 0xcc, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0xA000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offered
        );
        IPAddress offeredAddress = offered[0].Addresses[0].Address;

        Dhcpv6Message decline = NewMessage(Dhcpv6MessageTypeEnum.Decline, clientDuid, 0xA001);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses declineNa = NewNaRequest(1);
        declineNa.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = offeredAddress });
        decline.AddIdentityAssociationForNonTemporaryAddresses(declineNa);

        Dhcpv6Message declineReply = await _host.Handler.HandleRequestAsync(
            decline,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(declineReply); // Unlike DHCPv4, a DHCPv6 server replies to a DECLINE.

        // The pool's only address is quarantined, so even the declining client gets no offer
        // (there is no other free address to move its row to).
        Dhcpv6Message secondSolicit = NewSolicit(clientDuid, 0xA002);
        secondSolicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Dhcpv6Message secondAdvertise = await _host.Handler.HandleRequestAsync(
            secondSolicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(secondAdvertise);
    }

    [Fact]
    public async Task Confirm_ForAnOnLinkAddress_RepliesWithNoStatusCode()
    {
        await _host.SeedAddressScopeAsync("confirm-scope", "2001:db8:b::10", "2001:db8:b::12");

        Dhcpv6Message confirm = NewMessage(
            Dhcpv6MessageTypeEnum.Confirm,
            [0x00, 0x01, 0x00, 0x01, 0xdd, 0xdd, 0xdd, 0xdd, 0x00, 0x01],
            0xB000
        );
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = NewNaRequest(1);
        na.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse("2001:db8:b::11") }
        );
        confirm.AddIdentityAssociationForNonTemporaryAddresses(na);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            confirm,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.False(reply.TryGetStatusCode(out Dhcpv6StatusCodeEnum _, out string _));
    }

    [Fact]
    public async Task Confirm_ForAnOffLinkAddress_RepliesWithNotOnLink()
    {
        await _host.SeedAddressScopeAsync(
            "confirm-offlink-scope",
            "2001:db8:c::10",
            "2001:db8:c::12"
        );

        Dhcpv6Message confirm = NewMessage(
            Dhcpv6MessageTypeEnum.Confirm,
            [0x00, 0x01, 0x00, 0x01, 0xee, 0xee, 0xee, 0xee, 0x00, 0x01],
            0xC000
        );
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = NewNaRequest(1);
        na.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse("2001:db8:ff::11") }
        );
        confirm.AddIdentityAssociationForNonTemporaryAddresses(na);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            confirm,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.True(reply.TryGetStatusCode(out Dhcpv6StatusCodeEnum statusCode, out string _));
        Assert.Equal(Dhcpv6StatusCodeEnum.NotOnLink, statusCode);
    }

    [Fact]
    public async Task InformationRequest_RespondsWithDnsServersButNoIdentityAssociation()
    {
        await _host.SeedAddressScopeAsync(
            "inform-scope",
            "2001:db8:d::10",
            "2001:db8:d::12",
            dnsServerAddresses: ["2001:db8:d::1"]
        );

        Dhcpv6Message informationRequest = NewMessage(
            Dhcpv6MessageTypeEnum.InformationRequest,
            [0x00, 0x01, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x00, 0x01],
            0xD000
        );

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            informationRequest,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        Assert.True(reply.TryGetDnsServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(IPAddress.Parse("2001:db8:d::1"), Assert.Single(dnsServers));
        Assert.False(reply.TryGetIdentityAssociationsForNonTemporaryAddresses(out _));
    }

    [Fact]
    public async Task Solicit_ForPrefixDelegation_AdvertisesADelegatedPrefix()
    {
        await _host.SeedPrefixDelegationScopeAsync(
            "pd-scope",
            "2001:db8:1000::",
            "2001:db8:1fff::",
            delegatedPrefixLength: 60
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x11, 0x11, 0x11, 0x12, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0xE000);
        solicit.AddIdentityAssociationForPrefixDelegation(NewPdRequest(1));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(
            advertise.TryGetIdentityAssociationsForPrefixDelegation(
                out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
            )
        );
        Dhcpv6IdentityAssociationPrefix prefix = Assert.Single(
            Assert.Single(associations).Prefixes
        );
        Assert.Equal(60, prefix.PrefixLength);
        Assert.Equal(IPAddress.Parse("2001:db8:1000::"), prefix.Prefix);

        LeaseEntity lease = await _host.FindLeaseAsync("pd-scope", FormatDuid(clientDuid));
        Assert.NotNull(lease);
        Assert.Equal(60, lease.PrefixLength);
    }

    [Fact]
    public async Task Solicit_ThenRequest_ForPrefixDelegation_CommitsTheDelegatedPrefix()
    {
        await _host.SeedPrefixDelegationScopeAsync(
            "pd-request-scope",
            "2001:db8:2000::",
            "2001:db8:2fff::",
            delegatedPrefixLength: 60
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x22, 0x22, 0x22, 0x23, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0xF000);
        solicit.AddIdentityAssociationForPrefixDelegation(NewPdRequest(1));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
        advertise.TryGetServerIdentifier(out byte[] serverDuid);
        advertise.TryGetIdentityAssociationsForPrefixDelegation(
            out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> offeredAssociations
        );
        Dhcpv6IdentityAssociationPrefix offeredPrefix = offeredAssociations[0].Prefixes[0];

        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, clientDuid, 0xF000);
        request.SetServerIdentifier(serverDuid);
        Dhcpv6IdentityAssociationForPrefixDelegation requestPd = NewPdRequest(1);
        requestPd.Prefixes.Add(
            new Dhcpv6IdentityAssociationPrefix
            {
                Prefix = offeredPrefix.Prefix,
                PrefixLength = offeredPrefix.PrefixLength,
            }
        );
        request.AddIdentityAssociationForPrefixDelegation(requestPd);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        reply.TryGetIdentityAssociationsForPrefixDelegation(
            out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> repliedAssociations
        );
        Assert.Equal(offeredPrefix.Prefix, repliedAssociations[0].Prefixes[0].Prefix);

        LeaseEntity lease = await _host.FindLeaseAsync("pd-request-scope", FormatDuid(clientDuid));
        Assert.Equal(LeaseStateEnum.Active, lease.State);
        Assert.Equal(60, lease.PrefixLength);
    }

    /// <summary>
    ///     This method runs a SOLICIT and a REQUEST for a new client and returns its active lease,
    ///     so tests can start with an address in use.
    /// </summary>
    private async Task<LeaseEntity> ConfirmAddressForNewClientAsync(
        DhcpScopeEntity scope,
        byte[] clientDuid
    )
    {
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0xFF00);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
        advertise.TryGetServerIdentifier(out byte[] serverDuid);
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offered
        );
        IPAddress offeredAddress = offered[0].Addresses[0].Address;

        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, clientDuid, 0xFF00);
        request.SetServerIdentifier(serverDuid);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestNa = NewNaRequest(1);
        requestNa.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = offeredAddress });
        request.AddIdentityAssociationForNonTemporaryAddresses(requestNa);

        await _host.Handler.HandleRequestAsync(request, RemoteEndPoint, CancellationToken.None);

        return await _host.FindLeaseAsync(scope.Name, FormatDuid(clientDuid));
    }

    /// <summary>
    ///     This method gets the server DUID from the reply to an INFORMATION-REQUEST, which is
    ///     always answered, needs no scope and changes no state.
    /// </summary>
    private async Task<byte[]> GetServerDuidAsync()
    {
        Dhcpv6Message informationRequest = NewMessage(
            Dhcpv6MessageTypeEnum.InformationRequest,
            [0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
            0xFFFF
        );
        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            informationRequest,
            RemoteEndPoint,
            CancellationToken.None
        );
        reply.TryGetServerIdentifier(out byte[] serverDuid);
        return serverDuid;
    }

    /// <summary>
    ///     This method builds a SOLICIT with a Client Identifier and an Elapsed Time option, which
    ///     clients include in their messages (RFC 8415); other messages use
    ///     <see cref="NewMessage" />.
    /// </summary>
    private static Dhcpv6Message NewSolicit(byte[] clientDuid, uint transactionId)
    {
        Dhcpv6Message solicit = NewMessage(
            Dhcpv6MessageTypeEnum.Solicit,
            clientDuid,
            transactionId
        );
        solicit.SetElapsedTime(TimeSpan.Zero);
        return solicit;
    }

    /// <summary>
    ///     This method builds a message of the given type with a transaction ID and Client
    ///     Identifier.
    /// </summary>
    private static Dhcpv6Message NewMessage(
        Dhcpv6MessageTypeEnum messageType,
        byte[] clientDuid,
        uint transactionId
    )
    {
        Dhcpv6Message message = new() { MessageType = messageType, TransactionId = transactionId };
        message.SetClientIdentifier(clientDuid);
        return message;
    }

    /// <summary>
    ///     This method builds an IA_NA with only an IAID and no address hint, as in a client's
    ///     first SOLICIT.
    /// </summary>
    private static Dhcpv6IdentityAssociationForNonTemporaryAddresses NewNaRequest(
        uint identityAssociationId
    ) => new() { IdentityAssociationId = identityAssociationId };

    /// <summary>
    ///     This method builds an IA_PD with only an IAID and no prefix hint, like
    ///     <see cref="NewNaRequest" />.
    /// </summary>
    private static Dhcpv6IdentityAssociationForPrefixDelegation NewPdRequest(
        uint identityAssociationId
    ) => new() { IdentityAssociationId = identityAssociationId };

    /// <summary>
    ///     This method formats DUID bytes as colon-separated lowercase hex, as
    ///     <c>Dhcpv6ClientIdentifier</c> does, so tests can look leases up without using that
    ///     internal class.
    /// </summary>
    private static string FormatDuid(byte[] duid) =>
        string.Join(':', duid.Select(b => b.ToString("x2")));
}

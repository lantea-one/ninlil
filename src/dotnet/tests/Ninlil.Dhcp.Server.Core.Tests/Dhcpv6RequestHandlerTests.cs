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
    public async Task Solicit_WhenPoolExhausted_AdvertisesNoAddrsAvailWithBothIdentifiers()
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

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x33, 0x33, 0x33, 0x33, 0x00, 0x02];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x2000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(7));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        Assert.Equal(0x2000u, advertise.TransactionId);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = Assert.Single(
            NaAssociations(advertise)
        );
        Assert.Equal(7u, na.IdentityAssociationId);
        Assert.Empty(na.Addresses);
        Assert.True(na.Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum status, out string _));
        Assert.Equal(Dhcpv6StatusCodeEnum.NoAddressesAvailable, status);

        // RFC 8415 Section 18.3.9 asks for the status inside the IA only, so the message itself
        // carries none; the identifiers are there, and no lease row was created.
        Assert.False(advertise.TryGetStatusCode(out Dhcpv6StatusCodeEnum _, out string _));
        Assert.True(advertise.TryGetServerIdentifier(out byte[] _));
        Assert.True(advertise.TryGetClientIdentifier(out byte[] echoedClient));
        Assert.Equal(clientDuid, echoedClient);
        Assert.False(advertise.TryGetRapidCommit());
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(clientDuid)));
    }

    [Fact]
    public async Task Solicit_WhenPrefixPoolExhausted_AdvertisesNoPrefixAvail()
    {
        DhcpScopeEntity scope = await _host.SeedPrefixDelegationScopeAsync(
            "pd-full",
            "2001:db8:2000::",
            "2001:db8:2000::",
            delegatedPrefixLength: 60
        );
        Dhcpv6Message first = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x12, 0x12, 0x12, 0x12, 0x00, 0x01],
            0x2100
        );
        first.AddIdentityAssociationForPrefixDelegation(NewPdRequest(1));
        Dhcpv6Message firstAdvertise = await _host.Handler.HandleRequestAsync(
            first,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(firstAdvertise);

        Dhcpv6Message second = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x13, 0x13, 0x13, 0x13, 0x00, 0x02],
            0x2101
        );
        second.AddIdentityAssociationForPrefixDelegation(NewPdRequest(4));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            second,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        Assert.True(
            advertise.TryGetIdentityAssociationsForPrefixDelegation(
                out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> pds
            )
        );
        Dhcpv6IdentityAssociationForPrefixDelegation pd = Assert.Single(pds);
        Assert.Equal(4u, pd.IdentityAssociationId);
        Assert.Empty(pd.Prefixes);
        Assert.True(pd.Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum status, out string _));
        Assert.Equal(Dhcpv6StatusCodeEnum.NoPrefixAvailable, status);
        Assert.False(advertise.TryGetStatusCode(out Dhcpv6StatusCodeEnum _, out string _));
        Assert.True(advertise.TryGetServerIdentifier(out byte[] _));
        Assert.True(advertise.TryGetClientIdentifier(out byte[] _));
        Assert.NotNull(scope);
    }

    [Fact]
    public async Task Solicit_WithExhaustedAddressesButAFreePrefix_AdvertisesThePrefixAndTheNaStatus()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "mixed-na",
            "2001:db8:2::10",
            "2001:db8:2::10"
        );
        await _host.SeedPrefixDelegationScopeAsync(
            "mixed-pd",
            "2001:db8:3000::",
            "2001:db8:3fff::",
            delegatedPrefixLength: 60
        );
        await ConfirmAddressForNewClientAsync(
            scope,
            [0x00, 0x01, 0x00, 0x01, 0x14, 0x14, 0x14, 0x14, 0x00, 0x01]
        );

        Dhcpv6Message solicit = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x15, 0x15, 0x15, 0x15, 0x00, 0x02],
            0x2200
        );
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.AddIdentityAssociationForPrefixDelegation(NewPdRequest(2));

        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = Assert.Single(
            NaAssociations(advertise)
        );
        Assert.Empty(na.Addresses);
        Assert.True(na.Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum status, out string _));
        Assert.Equal(Dhcpv6StatusCodeEnum.NoAddressesAvailable, status);
        advertise.TryGetIdentityAssociationsForPrefixDelegation(
            out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> pds
        );
        Assert.Single(Assert.Single(pds).Prefixes);
    }

    [Fact]
    public async Task Solicit_OnALinkWithNoScope_GetsNoReply()
    {
        Dhcpv6Message solicit = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x16, 0x16, 0x16, 0x16, 0x00, 0x01],
            0x2300
        );
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        solicit.AddIdentityAssociationForPrefixDelegation(NewPdRequest(2));

        Assert.Null(
            await _host.Handler.HandleRequestAsync(solicit, RemoteEndPoint, CancellationToken.None)
        );
    }

    [Fact]
    public async Task Solicit_WithRapidCommitOnAnExhaustedScope_RepliesWithNoAddrsAvailAndRapidCommit()
    {
        // RFC 8415 Section 18.3.1: with Rapid Commit the server produces the REPLY "as though
        // it had received a Request message" and includes Rapid Commit in it; Section 18.3.2
        // then puts NoAddrsAvail in an IA that cannot be served.
        await using Dhcpv6EngineTestHost rapidHost = new();
        await rapidHost.InitializeAsync(enableRapidCommit: true);
        DhcpScopeEntity scope = await rapidHost.SeedAddressScopeAsync(
            "rapid-full",
            "2001:db8:4::10",
            "2001:db8:4::10"
        );
        Dhcpv6Message first = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x17, 0x17, 0x17, 0x17, 0x00, 0x01],
            0x2400
        );
        first.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        first.SetRapidCommit();
        Assert.NotNull(
            await rapidHost.Handler.HandleRequestAsync(
                first,
                RemoteEndPoint,
                CancellationToken.None
            )
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x18, 0x18, 0x18, 0x18, 0x00, 0x02];
        Dhcpv6Message second = NewSolicit(clientDuid, 0x2401);
        second.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        second.SetRapidCommit();
        Dhcpv6Message reply = await rapidHost.Handler.HandleRequestAsync(
            second,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        Assert.True(reply.TryGetRapidCommit());
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = Assert.Single(NaAssociations(reply));
        Assert.Empty(na.Addresses);
        Assert.True(na.Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum status, out string _));
        Assert.Equal(Dhcpv6StatusCodeEnum.NoAddressesAvailable, status);
        Assert.False(reply.TryGetStatusCode(out Dhcpv6StatusCodeEnum _, out string _));
        Assert.Null(await rapidHost.FindLeaseAsync(scope.Name, FormatDuid(clientDuid)));
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
    public async Task SolicitAndRequest_WithScopeCustomOption_BothRepliesCarryItAndTheLeaseStillWorks()
    {
        // End to end through the engine host: a custom option on a DHCPv6 scope reaches the
        // ADVERTISE and the REPLY, and the exchange still ends with an active lease.
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "request-custom-option",
            "2001:db8:c9::10",
            "2001:db8:c9::12"
        );
        await _host.SeedCustomOptionAsync(scope.Id, 32, "00000E10");

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x7a, 0x7a, 0x7a, 0x7a, 0x00, 0x01];
        Dhcpv6Message solicit = NewSolicit(clientDuid, 0x7000);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Dhcpv6Message advertise = await _host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(advertise);
        Assert.True(advertise.TryGetRawOption(32, out byte[] advertisedValue));
        Assert.Equal(Convert.FromHexString("00000E10"), advertisedValue);
        advertise.TryGetServerIdentifier(out byte[] serverDuid);
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offeredAssociations
        );

        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, clientDuid, 0x7001);
        request.SetServerIdentifier(serverDuid);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestNa = NewNaRequest(1);
        requestNa.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = offeredAssociations[0].Addresses[0].Address,
            }
        );
        request.AddIdentityAssociationForNonTemporaryAddresses(requestNa);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        Assert.True(reply.TryGetRawOption(32, out byte[] repliedValue));
        Assert.Equal(Convert.FromHexString("00000E10"), repliedValue);

        LeaseEntity lease = await _host.FindLeaseAsync(
            "request-custom-option",
            FormatDuid(clientDuid)
        );
        Assert.NotNull(lease);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
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
        release.SetServerIdentifier(await GetServerDuidAsync());
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
        decline.SetServerIdentifier(await GetServerDuidAsync());
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

        // The scope still exists, so the exhausted pool is answered with NoAddrsAvail.
        Assert.NotNull(secondAdvertise);
        Assert.Empty(Assert.Single(NaAssociations(secondAdvertise)).Addresses);
        Assert.Equal(Dhcpv6StatusCodeEnum.NoAddressesAvailable, NaStatus(secondAdvertise));
    }

    [Fact]
    public async Task Decline_OfAnotherClientsAddress_RepliesNoBindingAndChangesNothing()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "decline-foreign-v6",
            "2001:db8:c::10",
            "2001:db8:c::11"
        );
        byte[] ownerDuid = [0x00, 0x01, 0x00, 0x01, 0x10, 0x10, 0x10, 0x10, 0x00, 0x01];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, ownerDuid);

        byte[] otherDuid = [0x00, 0x01, 0x00, 0x01, 0x20, 0x20, 0x20, 0x20, 0x00, 0x02];
        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            await NewDeclineAsync(otherDuid, IPAddress.Parse(active.IpAddress)),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Equal(Dhcpv6StatusCodeEnum.NoBinding, NaStatus(reply));
        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, FormatDuid(ownerDuid));
        Assert.Equal(LeaseStateEnum.Active, after.State);
        Assert.Equal(active.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(otherDuid)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decline_WithTwoIas_AnswersEachOneAndQuarantinesOnlyTheClientsOwnAddress(
        bool ownAddressInSecondIa
    )
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "decline-two-ias",
            "2001:db8:c1::10",
            "2001:db8:c1::11"
        );
        byte[] ownerDuid = [0x00, 0x01, 0x00, 0x01, 0x61, 0x61, 0x61, 0x61, 0x00, 0x01];
        byte[] otherDuid = [0x00, 0x01, 0x00, 0x01, 0x62, 0x62, 0x62, 0x62, 0x00, 0x02];
        LeaseEntity own = await ConfirmAddressForNewClientAsync(scope, ownerDuid);
        LeaseEntity foreign = await ConfirmAddressForNewClientAsync(scope, otherDuid);

        Dhcpv6Message decline = NewMessage(Dhcpv6MessageTypeEnum.Decline, ownerDuid, 0xE100);
        decline.SetServerIdentifier(await GetServerDuidAsync());
        IPAddress ownAddress = IPAddress.Parse(own.IpAddress);
        IPAddress foreignAddress = IPAddress.Parse(foreign.IpAddress);
        (IPAddress first, IPAddress second) = ownAddressInSecondIa
            ? (foreignAddress, ownAddress)
            : (ownAddress, foreignAddress);

        foreach ((uint iaid, IPAddress address) in new[] { (1u, first), (2u, second) })
        {
            Dhcpv6IdentityAssociationForNonTemporaryAddresses na = NewNaRequest(iaid);
            na.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = address });
            decline.AddIdentityAssociationForNonTemporaryAddresses(na);
        }

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            decline,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> answered = NaAssociations(
            reply
        );
        Assert.Equal([1u, 2u], answered.Select(na => na.IdentityAssociationId));

        Dhcpv6StatusCodeEnum[] expected = ownAddressInSecondIa
            ? [Dhcpv6StatusCodeEnum.NoBinding, Dhcpv6StatusCodeEnum.Success]
            : [Dhcpv6StatusCodeEnum.Success, Dhcpv6StatusCodeEnum.NoBinding];

        for (int index = 0; index < answered.Count; index++)
        {
            Assert.True(
                answered[index]
                    .Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum status, out string _)
            );
            Assert.Equal(expected[index], status);
        }

        LeaseEntity quarantined = await _host.FindLeaseAsync(
            scope.Name,
            "declined:" + own.IpAddress
        );
        Assert.Equal(LeaseStateEnum.Expired, quarantined.State);
        LeaseEntity untouched = await _host.FindLeaseAsync(scope.Name, FormatDuid(otherDuid));
        Assert.Equal(LeaseStateEnum.Active, untouched.State);
    }

    [Fact]
    public async Task Decline_OfAFreeAddress_RepliesNoBindingAndCreatesNoRow()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "decline-free-v6",
            "2001:db8:d::10",
            "2001:db8:d::10"
        );
        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x30, 0x30, 0x30, 0x30, 0x00, 0x01];

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            await NewDeclineAsync(duid, IPAddress.Parse("2001:db8:d::10")),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Equal(Dhcpv6StatusCodeEnum.NoBinding, NaStatus(reply));
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(duid)));

        // The pool is still offered to another client.
        Dhcpv6Message solicit = NewSolicit(
            [0x00, 0x01, 0x00, 0x01, 0x31, 0x31, 0x31, 0x31, 0x00, 0x02],
            0xD000
        );
        solicit.AddIdentityAssociationForNonTemporaryAddresses(NewNaRequest(1));
        Assert.NotNull(
            await _host.Handler.HandleRequestAsync(solicit, RemoteEndPoint, CancellationToken.None)
        );
    }

    [Fact]
    public async Task Decline_WithAWrongOrMissingServerIdentifier_GetsNoReplyAndChangesNothing()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "decline-sid-v6",
            "2001:db8:e::10",
            "2001:db8:e::10"
        );
        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x40, 0x40, 0x40, 0x40, 0x00, 0x01];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, duid);
        IPAddress address = IPAddress.Parse(active.IpAddress);

        Dhcpv6Message wrong = await NewDeclineAsync(duid, address);
        wrong.SetServerIdentifier([0x00, 0x01, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x00, 0x99]);
        Assert.Null(
            await _host.Handler.HandleRequestAsync(wrong, RemoteEndPoint, CancellationToken.None)
        );

        Dhcpv6Message missing = NewMessage(Dhcpv6MessageTypeEnum.Decline, duid, 0xE001);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = NewNaRequest(1);
        na.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = address });
        missing.AddIdentityAssociationForNonTemporaryAddresses(na);
        Assert.Null(
            await _host.Handler.HandleRequestAsync(missing, RemoteEndPoint, CancellationToken.None)
        );

        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, FormatDuid(duid));
        Assert.Equal(LeaseStateEnum.Active, after.State);
    }

    [Fact]
    public async Task Decline_FromTheOwner_RepliesSuccessAndQuarantines()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "decline-owner-v6",
            "2001:db8:f::10",
            "2001:db8:f::10"
        );
        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x50, 0x50, 0x50, 0x50, 0x00, 0x01];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, duid);

        Dhcpv6Message reply = await _host.Handler.HandleRequestAsync(
            await NewDeclineAsync(duid, IPAddress.Parse(active.IpAddress)),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Equal(Dhcpv6StatusCodeEnum.Success, NaStatus(reply));

        // The quarantine row belongs to no client, so the owner's own slot is free again.
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(duid)));
        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, "declined:" + active.IpAddress);
        Assert.Equal(LeaseStateEnum.Expired, after.State);
        Assert.True(after.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Release_WithAWrongServerIdentifier_GetsNoReplyAndLeavesTheLeaseUnchanged()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "release-sid-v6",
            "2001:db8:10::10",
            "2001:db8:10::10"
        );
        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x60, 0x60, 0x60, 0x60, 0x00, 0x01];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, duid);

        Dhcpv6Message release = NewMessage(Dhcpv6MessageTypeEnum.Release, duid, 0x1001);
        release.SetServerIdentifier([0x00, 0x01, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x00, 0x99]);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = NewNaRequest(1);
        na.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress { Address = IPAddress.Parse(active.IpAddress) }
        );
        release.AddIdentityAssociationForNonTemporaryAddresses(na);

        Assert.Null(
            await _host.Handler.HandleRequestAsync(release, RemoteEndPoint, CancellationToken.None)
        );
        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, FormatDuid(duid));
        Assert.Equal(LeaseStateEnum.Active, after.State);
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
    ///     This method builds a DECLINE of one address, carrying this server's Server Identifier.
    /// </summary>
    private async Task<Dhcpv6Message> NewDeclineAsync(byte[] clientDuid, IPAddress address)
    {
        Dhcpv6Message decline = NewMessage(Dhcpv6MessageTypeEnum.Decline, clientDuid, 0xE000);
        decline.SetServerIdentifier(await GetServerDuidAsync());
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = NewNaRequest(1);
        na.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = address });
        decline.AddIdentityAssociationForNonTemporaryAddresses(na);
        return decline;
    }

    /// <summary>
    ///     This method returns every IA_NA of a message, in order.
    /// </summary>
    private static IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> NaAssociations(
        Dhcpv6Message message
    )
    {
        Assert.True(
            message.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
            )
        );
        return associations;
    }

    /// <summary>
    ///     This method reads the Status Code of the first IA_NA in a reply.
    /// </summary>
    private static Dhcpv6StatusCodeEnum NaStatus(Dhcpv6Message reply)
    {
        Assert.NotNull(reply);
        reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
        );
        Assert.True(
            associations[0]
                .Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum statusCode, out string _)
        );
        return statusCode;
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

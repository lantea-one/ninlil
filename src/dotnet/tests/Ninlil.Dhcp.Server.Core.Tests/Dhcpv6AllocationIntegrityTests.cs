using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the DHCPv6 allocation rules that keep lease rows consistent, the
///     counterpart of <see cref="Dhcpv4AllocationIntegrityTests" />: a declined address stays
///     quarantined under a <c>declined:&lt;address&gt;</c> row, another client's stale row never
///     makes an ADVERTISE fail on the unique index (for addresses and prefixes), and a
///     reservation outranks the client's current binding. It sends real messages through
///     <see cref="Dhcpv6RequestHandler" /> on a <see cref="Dhcpv6EngineTestHost" /> (temporary
///     SQLite file with the unique indexes enforced) and seeds special rows through the
///     repositories.
/// </summary>
public sealed class Dhcpv6AllocationIntegrityTests : IAsyncLifetime
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
    public async Task Decline_KeepsTheAddressQuarantinedForEveryClientAndRefusesTheDecliner()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "v6-integrity-decline",
            "2001:db8:1::10",
            "2001:db8:1::12"
        );
        byte[] decliner = Duid(1);
        IPAddress declined = AddressOf(await SolicitAsync(decliner));
        Assert.Equal(IPAddress.Parse("2001:db8:1::10"), declined);
        await RequestAsync(decliner, declined);
        _host.PublishedEvents.Clear();

        Dhcpv6Message declineReply = await SendAsync(await DeclineAsync(decliner, declined));
        Assert.Equal(Dhcpv6StatusCodeEnum.Success, NaStatus(declineReply));

        // One Expired event for the declined row.
        DhcpLeaseChangedEvent changed = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Expired, changed.ChangeKind);

        // The decliner's next SOLICIT is not offered the declined address.
        IPAddress again = AddressOf(await SolicitAsync(decliner));
        Assert.NotEqual(declined, again);

        // The quarantine row outlives the decliner's move to a new address.
        LeaseEntity quarantine = await _host.FindLeaseAsync(scope.Name, "declined:" + declined);
        Assert.NotNull(quarantine);
        Assert.Equal(LeaseStateEnum.Expired, quarantine.State);
        Assert.True(quarantine.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);

        // Another client hinting at the declined address does not get it.
        IPAddress other = AddressOf(await SolicitAsync(Duid(2), declined));
        Assert.NotEqual(declined, other);

        // The decliner cannot be granted the address it declined.
        Dhcpv6Message reply = await RequestAsync(decliner, declined);
        Assert.Equal(Dhcpv6StatusCodeEnum.NoBinding, NaStatus(reply));
    }

    [Fact]
    public async Task Solicit_ForAClientWhoseRowIsMovingOntoAnotherClientsStaleRow_Advertises()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "v6-integrity-stale",
            "2001:db8:2::20",
            "2001:db8:2::25"
        );
        byte[] clientA = Duid(3);
        byte[] clientB = Duid(4);
        await SeedLeaseAsync(
            scope,
            FormatDuid(clientA),
            "2001:db8:2::20",
            null,
            LeaseStateEnum.Released,
            TimeSpan.Zero
        );

        // B holds ::21, which an operator has since reserved for C, so B cannot keep it.
        await SeedLeaseAsync(
            scope,
            FormatDuid(clientB),
            "2001:db8:2::21",
            null,
            LeaseStateEnum.Active,
            TimeSpan.FromHours(1)
        );
        await _host.SeedReservationAsync(scope.Id, FormatDuid(Duid(5)), "2001:db8:2::21");

        IPAddress offered = AddressOf(
            await SolicitAsync(clientB, IPAddress.Parse("2001:db8:2::20"))
        );

        Assert.Equal(IPAddress.Parse("2001:db8:2::20"), offered);
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(clientA)));
        LeaseEntity b = await _host.FindLeaseAsync(scope.Name, FormatDuid(clientB));
        Assert.Equal("2001:db8:2::20", b.IpAddress);
        Assert.Equal(LeaseStateEnum.Offered, b.State);
    }

    [Fact]
    public async Task Solicit_ForAClientWithALeaseAndANewReservation_AdvertisesAndGrantsTheReservation()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "v6-integrity-reserve",
            "2001:db8:3::30",
            "2001:db8:3::45"
        );
        byte[] client = Duid(6);
        IPAddress first = AddressOf(await SolicitAsync(client));
        await RequestAsync(client, first);
        Assert.Equal(IPAddress.Parse("2001:db8:3::30"), first);

        await _host.SeedReservationAsync(scope.Id, FormatDuid(client), "2001:db8:3::40");

        IPAddress offer = AddressOf(await SolicitAsync(client));
        Assert.Equal(IPAddress.Parse("2001:db8:3::40"), offer);

        Dhcpv6Message reply = await RequestAsync(client, offer);
        Assert.Equal(offer, AddressOf(reply));
        LeaseEntity lease = await _host.FindLeaseAsync(scope.Name, FormatDuid(client));
        Assert.Equal("2001:db8:3::40", lease.IpAddress);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
    }

    [Fact]
    public async Task Solicit_ForAClientWhoseAddressWasReservedForAnotherClient_AdvertisesAnotherAddress()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "v6-integrity-taken",
            "2001:db8:4::50",
            "2001:db8:4::55"
        );
        byte[] client = Duid(7);
        IPAddress first = AddressOf(await SolicitAsync(client));
        await RequestAsync(client, first);
        Assert.Equal(IPAddress.Parse("2001:db8:4::50"), first);

        await _host.SeedReservationAsync(scope.Id, FormatDuid(Duid(8)), "2001:db8:4::50");

        IPAddress offer = AddressOf(await SolicitAsync(client));
        Assert.NotEqual(IPAddress.Parse("2001:db8:4::50"), offer);

        Dhcpv6Message reply = await RequestAsync(client, offer);
        Assert.Equal(offer, AddressOf(reply));
    }

    [Fact]
    public async Task Solicit_WithAQuarantineStoredOnTheClientsOwnRow_MovesItAndAdvertisesAnotherAddress()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "v6-integrity-legacy",
            "2001:db8:5::60",
            "2001:db8:5::65"
        );
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(9)),
            "2001:db8:5::60",
            null,
            LeaseStateEnum.Expired,
            TimeSpan.FromMinutes(5)
        );

        IPAddress offer = AddressOf(await SolicitAsync(Duid(9)));

        Assert.NotEqual(IPAddress.Parse("2001:db8:5::60"), offer);
        LeaseEntity quarantine = await _host.FindLeaseAsync(scope.Name, "declined:2001:db8:5::60");
        Assert.NotNull(quarantine);
        Assert.True(quarantine.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Solicit_ForAClientWhoseLeaseExpiredNaturally_AdvertisesItsPreviousAddress()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "v6-integrity-previous",
            "2001:db8:6::69",
            "2001:db8:6::75"
        );
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(10)),
            "2001:db8:6::70",
            null,
            LeaseStateEnum.Expired,
            TimeSpan.FromMinutes(-5)
        );

        IPAddress offer = AddressOf(await SolicitAsync(Duid(10)));

        Assert.Equal(IPAddress.Parse("2001:db8:6::70"), offer);
    }

    [Fact]
    public async Task Solicit_ForPrefixDelegationWhoseNextFreePrefixIsAnotherClientsStaleRow_Advertises()
    {
        DhcpScopeEntity scope = await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd",
            "2001:db8:3000::",
            "2001:db8:3fff::",
            delegatedPrefixLength: 60
        );

        // The first prefix is in use by a third client and the second is a released row of
        // another client. The requesting client's own row is expired and out of the pool, so
        // the scan picks the second prefix and must remove the other client's row.
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(11)),
            "2001:db8:3000::",
            60,
            LeaseStateEnum.Active,
            TimeSpan.FromHours(1)
        );
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(12)),
            "2001:db8:3000:10::",
            60,
            LeaseStateEnum.Released,
            TimeSpan.Zero
        );
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(13)),
            "2001:db8:ffff::",
            60,
            LeaseStateEnum.Expired,
            TimeSpan.FromMinutes(-5)
        );

        Dhcpv6Message solicit = NewSolicit(Duid(13), 0x30);
        solicit.AddIdentityAssociationForPrefixDelegation(
            new Dhcpv6IdentityAssociationForPrefixDelegation { IdentityAssociationId = 1 }
        );
        Dhcpv6Message advertise = await SendAsync(solicit);

        Assert.NotNull(advertise);
        Assert.True(
            advertise.TryGetIdentityAssociationsForPrefixDelegation(
                out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
            )
        );
        Assert.Equal(
            IPAddress.Parse("2001:db8:3000:10::"),
            Assert.Single(Assert.Single(associations).Prefixes).Prefix
        );
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(Duid(12))));
        LeaseEntity own = await _host.FindLeaseAsync(scope.Name, FormatDuid(Duid(13)));
        Assert.Equal("2001:db8:3000:10::", own.IpAddress);
        Assert.Equal(LeaseStateEnum.Offered, own.State);
    }

    [Fact]
    public async Task Solicit_ForPrefixDelegationWithAMisalignedHint_AdvertisesAnAlignedPrefix()
    {
        await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd-misaligned",
            "2001:db8::",
            "2001:db8:0:ffff:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 56
        );

        Dhcpv6IdentityAssociationPrefix granted = await SolicitPrefixAsync(
            Duid(20),
            IPAddress.Parse("2001:db8:0:1::"),
            56
        );

        Assert.Equal(IPAddress.Parse("2001:db8::"), granted.Prefix);
        Assert.Equal(56, granted.PrefixLength);
    }

    [Fact]
    public async Task Solicit_ForPrefixDelegationWithAnAlignedHint_AdvertisesTheHint()
    {
        await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd-aligned",
            "2001:db8::",
            "2001:db8:0:ffff:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 56
        );

        Dhcpv6IdentityAssociationPrefix granted = await SolicitPrefixAsync(
            Duid(21),
            IPAddress.Parse("2001:db8:0:100::"),
            56
        );

        Assert.Equal(IPAddress.Parse("2001:db8:0:100::"), granted.Prefix);
    }

    [Fact]
    public async Task Solicit_ForPrefixDelegationWithAHintOfAnotherLength_IgnoresTheHint()
    {
        await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd-length",
            "2001:db8::",
            "2001:db8:0:ffff:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 56
        );

        Dhcpv6IdentityAssociationPrefix granted = await SolicitPrefixAsync(
            Duid(22),
            IPAddress.Parse("2001:db8:0:100::"),
            60
        );

        Assert.Equal(IPAddress.Parse("2001:db8::"), granted.Prefix);
        Assert.Equal(56, granted.PrefixLength);
    }

    [Fact]
    public async Task Solicit_ForPrefixDelegationWithABlockingShorterPrefixRow_SkipsEveryPrefixInsideIt()
    {
        DhcpScopeEntity scope = await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd-overlap",
            "2001:db8::",
            "2001:db8:0:ffff:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 56
        );

        // A /52 left from an older DelegatedPrefixLength covers sixteen /56 blocks.
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(23)),
            "2001:db8::",
            52,
            LeaseStateEnum.Active,
            TimeSpan.FromHours(1)
        );

        Dhcpv6IdentityAssociationPrefix granted = await SolicitPrefixAsync(Duid(24), null, 0);

        Assert.Equal(IPAddress.Parse("2001:db8:0:1000::"), granted.Prefix);
    }

    [Fact]
    public async Task Request_ForAMisalignedPrefix_RepliesNoBindingAndCreatesNoRow()
    {
        DhcpScopeEntity scope = await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd-request-misaligned",
            "2001:db8::",
            "2001:db8:0:ffff:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 56
        );

        Dhcpv6IdentityAssociationForPrefixDelegation reply = await RequestPrefixAsync(
            Duid(25),
            IPAddress.Parse("2001:db8:0:1::"),
            56
        );

        Assert.Empty(reply.Prefixes);
        Assert.True(reply.Options.TryGetStatusCode(out Dhcpv6StatusCodeEnum status, out string _));
        Assert.Equal(Dhcpv6StatusCodeEnum.NoBinding, status);
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(Duid(25))));
    }

    [Fact]
    public async Task Request_ForAPrefixInsideAnotherClientsBlockingRow_RepliesNoBinding()
    {
        DhcpScopeEntity scope = await _host.SeedPrefixDelegationScopeAsync(
            "v6-integrity-pd-request-overlap",
            "2001:db8::",
            "2001:db8:0:ffff:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 56
        );
        await SeedLeaseAsync(
            scope,
            FormatDuid(Duid(26)),
            "2001:db8::",
            52,
            LeaseStateEnum.Active,
            TimeSpan.FromHours(1)
        );

        Dhcpv6IdentityAssociationForPrefixDelegation reply = await RequestPrefixAsync(
            Duid(27),
            IPAddress.Parse("2001:db8:0:100::"),
            56
        );

        Assert.Empty(reply.Prefixes);
        Assert.Null(await _host.FindLeaseAsync(scope.Name, FormatDuid(Duid(27))));
    }

    /// <summary>
    ///     This method sends a SOLICIT with an IA_PD (carrying an IAPREFIX hint when an address is
    ///     given) and returns the one prefix the ADVERTISE grants.
    /// </summary>
    private async Task<Dhcpv6IdentityAssociationPrefix> SolicitPrefixAsync(
        byte[] client,
        IPAddress hint,
        int hintLength
    )
    {
        Dhcpv6Message solicit = NewSolicit(client, 0x40);
        Dhcpv6IdentityAssociationForPrefixDelegation pd = new() { IdentityAssociationId = 1 };

        if (hint is not null)
            pd.Prefixes.Add(
                new Dhcpv6IdentityAssociationPrefix
                {
                    Prefix = hint,
                    PrefixLength = (byte)hintLength,
                }
            );

        solicit.AddIdentityAssociationForPrefixDelegation(pd);
        Dhcpv6Message advertise = await SendAsync(solicit);

        Assert.NotNull(advertise);
        Assert.True(
            advertise.TryGetIdentityAssociationsForPrefixDelegation(
                out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
            )
        );
        return Assert.Single(Assert.Single(associations).Prefixes);
    }

    /// <summary>
    ///     This method sends a REQUEST naming one prefix and returns the reply's IA_PD.
    /// </summary>
    private async Task<Dhcpv6IdentityAssociationForPrefixDelegation> RequestPrefixAsync(
        byte[] client,
        IPAddress prefix,
        int prefixLength
    )
    {
        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, client, 0x41);
        request.SetServerIdentifier(await GetServerDuidAsync());
        Dhcpv6IdentityAssociationForPrefixDelegation pd = new() { IdentityAssociationId = 1 };
        pd.Prefixes.Add(
            new Dhcpv6IdentityAssociationPrefix
            {
                Prefix = prefix,
                PrefixLength = (byte)prefixLength,
            }
        );
        request.AddIdentityAssociationForPrefixDelegation(pd);
        Dhcpv6Message reply = await SendAsync(request);

        Assert.NotNull(reply);
        Assert.True(
            reply.TryGetIdentityAssociationsForPrefixDelegation(
                out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
            )
        );
        return Assert.Single(associations);
    }

    /// <summary>
    ///     This method saves a lease row through the repository with an expiry relative to now.
    /// </summary>
    private async Task SeedLeaseAsync(
        DhcpScopeEntity scope,
        string clientIdentifier,
        string ipAddress,
        int? prefixLength,
        LeaseStateEnum state,
        TimeSpan expiresIn
    )
    {
        await using AsyncServiceScope diScope = _host.Host.Services.CreateAsyncScope();
        await diScope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    ScopeName = scope.Name,
                    ClientIdentifier = clientIdentifier,
                    IpAddress = ipAddress,
                    PrefixLength = prefixLength,
                    State = state,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow + expiresIn,
                }
            );
        await diScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method sends a SOLICIT with an IA_NA (and an IAADDR hint, if given) and returns
    ///     the reply.
    /// </summary>
    private async Task<Dhcpv6Message> SolicitAsync(byte[] client, IPAddress hint = null)
    {
        Dhcpv6Message solicit = NewSolicit(client, 0x1);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = new() { IdentityAssociationId = 1 };

        if (hint is not null)
            na.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = hint });

        solicit.AddIdentityAssociationForNonTemporaryAddresses(na);
        return await SendAsync(solicit);
    }

    /// <summary>
    ///     This method sends a REQUEST for one address, carrying this server's identifier.
    /// </summary>
    private async Task<Dhcpv6Message> RequestAsync(byte[] client, IPAddress address)
    {
        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, client, 0x2);
        request.SetServerIdentifier(await GetServerDuidAsync());
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = new() { IdentityAssociationId = 1 };
        na.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = address });
        request.AddIdentityAssociationForNonTemporaryAddresses(na);
        return await SendAsync(request);
    }

    /// <summary>
    ///     This method builds a DECLINE of one address, carrying this server's identifier.
    /// </summary>
    private async Task<Dhcpv6Message> DeclineAsync(byte[] client, IPAddress address)
    {
        Dhcpv6Message decline = NewMessage(Dhcpv6MessageTypeEnum.Decline, client, 0x3);
        decline.SetServerIdentifier(await GetServerDuidAsync());
        Dhcpv6IdentityAssociationForNonTemporaryAddresses na = new() { IdentityAssociationId = 1 };
        na.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = address });
        decline.AddIdentityAssociationForNonTemporaryAddresses(na);
        return decline;
    }

    /// <summary>
    ///     This method reads the first address of the first IA_NA in a reply.
    /// </summary>
    private static IPAddress AddressOf(Dhcpv6Message reply)
    {
        Assert.NotNull(reply);
        Assert.True(
            reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
            )
        );
        return associations[0].Addresses[0].Address;
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
        Dhcpv6Message reply = await SendAsync(
            NewMessage(Dhcpv6MessageTypeEnum.InformationRequest, Duid(0), 0xFFFF)
        );
        reply.TryGetServerIdentifier(out byte[] serverDuid);
        return serverDuid;
    }

    private ValueTask<Dhcpv6Message> SendAsync(Dhcpv6Message message) =>
        _host.Handler.HandleRequestAsync(message, RemoteEndPoint, CancellationToken.None);

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
    ///     This method builds a distinct DUID-LLT-shaped client identifier per number.
    /// </summary>
    private static byte[] Duid(byte number) =>
        [0x00, 0x01, 0x00, 0x01, 0xb0, 0xb0, 0xb0, 0xb0, 0x00, number];

    private static string FormatDuid(byte[] duid) =>
        string.Join(':', duid.Select(b => b.ToString("x2")));
}

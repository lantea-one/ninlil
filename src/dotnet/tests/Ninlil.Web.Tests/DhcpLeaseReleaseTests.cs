using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpLeasesController.Release" /> on the shared in-process
///     host, signed in as the bootstrap administrator. A lease is granted by the host's DHCPv4
///     engine through a DISCOVER and REQUEST passed straight to the registered
///     <see cref="IDhcpv4RequestHandler" /> (no sockets), as
///     <c>Dhcpv4RequestHandlerTests</c> in <c>Ninlil.Dhcp.Server.Core.Tests</c> does. After the
///     release through the API, a DISCOVER from another client is offered the same address, which
///     shows the address is free and not just that the row's <c>State</c> changed.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpLeaseReleaseTests(NinlilDnsWebApplicationFactory factory)
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

    /// <summary>
    ///     The first address of the test scope's subnet, set as the relay agent address
    ///     (<c>giaddr</c>) of every message. Without a relay address the engine falls back to "the
    ///     single enabled scope" only when exactly one DHCPv4 scope exists (see
    ///     <c>Ninlil.Dhcp.Server.Core.Scope.Dhcpv4ScopeResolver</c>), and other test classes add
    ///     scopes to the shared host. With <c>giaddr</c> inside this subnet, the scope is chosen by
    ///     the RFC 2131 section 4.1 rule however many other scopes exist.
    /// </summary>
    private static readonly IPAddress RelayAgentAddress = IPAddress.Parse("192.168.77.1");

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();

        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    [Fact]
    public async Task Release_ForARealGrantedLease_MakesTheAddressGenuinelyAvailableAgain()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"release-scope-{Guid.NewGuid():N}";

        // A single-address pool, so a second client's DISCOVER can only succeed once the release
        // has freed this address.
        HttpResponseMessage scopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = scopeName,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.77.50",
                rangeEndAddress = "192.168.77.50",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        scopeResponse.EnsureSuccessStatusCode();

        // Grant a lease through the engine with DISCOVER, REQUEST and ACK, not a saved row.
        IDhcpv4RequestHandler handler =
            factory.Services.GetRequiredService<IDhcpv4RequestHandler>();
        byte[] clientHardwareAddress = [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x50];

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x9000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.RelayAgentIpAddress = RelayAgentAddress;
        Dhcpv4Message offer = await handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x9000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.RelayAgentIpAddress = RelayAgentAddress;
        request.SetRequestedIpAddress(offer.YourIpAddress);
        Dhcpv4Message ack = await handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum ackType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, ackType);

        LeaseEntity grantedLease;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            grantedLease = await leaseRepository.FindByClientIdentifierAsync(
                scopeName,
                "aa:bb:cc:dd:ee:50"
            );
        }
        Assert.NotNull(grantedLease);
        Assert.Equal(LeaseStateEnum.Active, grantedLease.State);

        // The pool is now full, so another client's DISCOVER gets no offer.
        byte[] otherClientHardwareAddress = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        Dhcpv4Message discoverBeforeRelease = NewMessage(otherClientHardwareAddress, 0x9001);
        discoverBeforeRelease.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discoverBeforeRelease.RelayAgentIpAddress = RelayAgentAddress;
        Dhcpv4Message offerBeforeRelease = await handler.HandleRequestAsync(
            discoverBeforeRelease,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.Null(offerBeforeRelease);

        // Release the lease through the API.
        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{grantedLease.Id}/release",
            content: null
        );
        releaseResponse.EnsureSuccessStatusCode();
        DhcpLeaseDetail released =
            await releaseResponse.Content.ReadFromJsonAsync<DhcpLeaseDetail>();
        Assert.Equal(LeaseStateEnum.Released, released.State);

        // The other client's DISCOVER now succeeds and is offered the same address; the engine
        // decides, not a check on the database row.
        Dhcpv4Message discoverAfterRelease = NewMessage(otherClientHardwareAddress, 0x9002);
        discoverAfterRelease.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discoverAfterRelease.RelayAgentIpAddress = RelayAgentAddress;
        Dhcpv4Message offerAfterRelease = await handler.HandleRequestAsync(
            discoverAfterRelease,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offerAfterRelease);
        Assert.Equal(IPAddress.Parse("192.168.77.50"), offerAfterRelease.YourIpAddress);
    }

    [Fact]
    public async Task Release_ForAnAlreadyReleasedLease_IsAnIdempotentNoOp()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"release-idempotent-scope-{Guid.NewGuid():N}";

        Guid leaseId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = leaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:99",
                    IpAddress = "192.168.78.10",
                    State = LeaseStateEnum.Released,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{leaseId}/release",
            content: null
        );
        releaseResponse.EnsureSuccessStatusCode();
        DhcpLeaseDetail released =
            await releaseResponse.Content.ReadFromJsonAsync<DhcpLeaseDetail>();
        Assert.Equal(LeaseStateEnum.Released, released.State);
    }

    [Fact]
    public async Task Release_ForANonexistentLease_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{Guid.NewGuid()}/release",
            content: null
        );

        Assert.Equal(HttpStatusCode.NotFound, releaseResponse.StatusCode);
    }

    [Fact]
    public async Task Release_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{Guid.NewGuid()}/release",
            content: null
        );

        Assert.Equal(HttpStatusCode.Unauthorized, releaseResponse.StatusCode);
    }

    /// <summary>
    ///     This method builds a request message, as the helper in
    ///     <c>Ninlil.Dhcp.Server.Core.Tests.Dhcpv4RequestHandlerTests</c> does.
    /// </summary>
    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
}

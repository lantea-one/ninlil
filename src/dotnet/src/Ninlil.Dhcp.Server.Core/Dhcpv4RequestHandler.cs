using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dhcp.Server.Core.Options;
using Ninlil.Dhcp.Server.Core.Scope;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core;

/// <summary>
///     This class is the DHCPv4 server engine, the <see cref="IDhcpv4RequestHandler" /> that
///     <see cref="Dhcpv4UdpListener" /> passes every decoded request to. It handles the RFC 2131
///     Section 4.3 message types (DISCOVER, REQUEST, DECLINE, RELEASE, INFORM) and builds the
///     replies, leaving the decisions to <see cref="IDhcpv4ScopeResolver" /> (which scope),
///     <see cref="IDhcpv4LeaseAllocationService" /> (which address),
///     <see cref="Dhcpv4OptionDeliveryService" /> (standard options) and
///     <see cref="IDhcpv4OptionOverlayService" /> (custom options and tag rules, applied after
///     the standard ones).
///     <para>
///         <b>A scope per request.</b> The listener is a singleton and holds this singleton
///         handler, but the scope resolver, allocation service and overlay service are scoped,
///         because they use the <c>DbContext</c>-backed repositories through <c>IUnitOfWork</c>,
///         which must not be shared across concurrent requests. Injecting them into a singleton
///         would make one <c>DbContext</c> live for the whole process. The handler therefore
///         takes an <see cref="IServiceScopeFactory" /> and creates a scope for each
///         <see cref="HandleRequestAsync" /> call, as
///         <c>Ninlil.Dns.Server.Authoritative.Cache.AuthoritativeZoneCache</c> does.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     Used to create a scope for every request, so scoped services are not captured by this
///     singleton.
/// </param>
/// <param name="serverIdentifierProvider">The server's Server Identifier address.</param>
/// <param name="logger">The logger ignored requests are recorded to.</param>
public sealed class Dhcpv4RequestHandler(
    IServiceScopeFactory scopeFactory,
    IDhcpServerIdentifierProvider serverIdentifierProvider,
    ILogger<Dhcpv4RequestHandler> logger
) : IDhcpv4RequestHandler
{
    /// <inheritdoc />
    public async ValueTask<Dhcpv4Message> HandleRequestAsync(
        Dhcpv4Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        if (!request.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType))
        {
            logger.LogDebug(
                "Ignoring a DHCPv4 message from {RemoteEndPoint} with no (or an unreadable) DHCP Message Type option.",
                remoteEndPoint
            );
            return null;
        }

        // One scope per request (see the class summary).
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDhcpv4ScopeResolver scopeResolver =
            scope.ServiceProvider.GetRequiredService<IDhcpv4ScopeResolver>();
        IDhcpv4LeaseAllocationService leaseAllocationService =
            scope.ServiceProvider.GetRequiredService<IDhcpv4LeaseAllocationService>();
        IDhcpv4OptionOverlayService optionOverlayService =
            scope.ServiceProvider.GetRequiredService<IDhcpv4OptionOverlayService>();

        return messageType switch
        {
            Dhcpv4MessageTypeEnum.Discover => await HandleDiscoverAsync(
                request,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                cancellationToken
            ),
            Dhcpv4MessageTypeEnum.Request => await HandleRequestMessageAsync(
                request,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                cancellationToken
            ),
            Dhcpv4MessageTypeEnum.Decline => await HandleDeclineAsync(
                request,
                scopeResolver,
                leaseAllocationService,
                cancellationToken
            ),
            Dhcpv4MessageTypeEnum.Release => await HandleReleaseAsync(
                request,
                scopeResolver,
                leaseAllocationService,
                cancellationToken
            ),
            Dhcpv4MessageTypeEnum.Inform => await HandleInformAsync(
                request,
                scopeResolver,
                optionOverlayService,
                cancellationToken
            ),

            // OFFER, ACK and NAK come from servers; one received here is malformed or hostile and
            // gets no answer.
            _ => null,
        };
    }

    /// <summary>
    ///     This method handles a DHCPDISCOVER (RFC 2131 Section 4.3.1): resolve the scope, hold an
    ///     address for the client and return an OFFER with the negotiated lease time and options.
    ///     No scope or no free address means no reply.
    /// </summary>
    private async Task<Dhcpv4Message> HandleDiscoverAsync(
        Dhcpv4Message request,
        IDhcpv4ScopeResolver scopeResolver,
        IDhcpv4LeaseAllocationService leaseAllocationService,
        IDhcpv4OptionOverlayService optionOverlayService,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, cancellationToken);

        if (scope is null)
            return null;

        LeaseEntity lease = await leaseAllocationService.OfferAsync(
            scope,
            request,
            cancellationToken
        );

        if (lease is null)
            return null;

        TimeSpan leaseDuration = Dhcpv4LeaseTimeNegotiator.Negotiate(scope, request);

        Dhcpv4Message offer = NewReply(request, IPAddress.Parse(lease.IpAddress));
        offer.SetMessageType(Dhcpv4MessageTypeEnum.Offer);
        Dhcpv4OptionDeliveryService.Apply(
            offer,
            scope,
            request,
            serverIdentifierProvider.Value,
            leaseDuration
        );
        await optionOverlayService.ApplyOverlaysAsync(offer, scope, request, cancellationToken);

        return offer;
    }

    /// <summary>
    ///     This method handles a DHCPREQUEST. It classifies it as one of the RFC 2131 Section 4.3.2
    ///     cases (selecting, renewing or rebinding, init-reboot) from the Server Identifier option,
    ///     <c>ciaddr</c> and the Requested IP Address option, confirms the address and returns an
    ///     ACK, or a NAK when the address is not available to the client.
    /// </summary>
    private async Task<Dhcpv4Message> HandleRequestMessageAsync(
        Dhcpv4Message request,
        IDhcpv4ScopeResolver scopeResolver,
        IDhcpv4LeaseAllocationService leaseAllocationService,
        IDhcpv4OptionOverlayService optionOverlayService,
        CancellationToken cancellationToken
    )
    {
        bool hasServerIdentifier = request.TryGetServerIdentifier(out IPAddress requestedServer);
        bool hasRequestedAddress = request.TryGetRequestedIpAddress(out IPAddress requestedAddress);
        bool hasClientAddress = !request.ClientIpAddress.Equals(IPAddress.Any);

        IPAddress addressToConfirm;

        if (hasServerIdentifier)
        {
            // SELECTING: the client accepts one server's OFFER. If it chose another server,
            // stay silent (RFC 2131 Section 4.3.2).
            if (!requestedServer.Equals(serverIdentifierProvider.Value))
                return null;

            if (!hasRequestedAddress)
                return null;

            addressToConfirm = requestedAddress;
        }
        else if (hasClientAddress)
        {
            // RENEWING or REBINDING: the client holds ciaddr and asks to extend the lease.
            addressToConfirm = request.ClientIpAddress;
        }
        else if (hasRequestedAddress)
        {
            // INIT-REBOOT: after a reboot the client checks that a remembered address is still
            // valid, with neither ciaddr nor a server identifier.
            addressToConfirm = requestedAddress;
        }
        else
        {
            // None of the RFC 2131 cases applies.
            logger.LogDebug(
                "Ignoring a malformed DHCPREQUEST that carries neither a Server Identifier, a client address, nor a Requested IP Address option."
            );
            return null;
        }

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, cancellationToken);

        if (scope is null)
            // No scope for this client: RFC 2131 Section 4.3.2 calls for silence, not a NAK,
            // from a server with no knowledge of the client's network.
            return null;

        TimeSpan leaseDuration = Dhcpv4LeaseTimeNegotiator.Negotiate(scope, request);
        LeaseEntity lease = await leaseAllocationService.ConfirmAsync(
            scope,
            request,
            addressToConfirm,
            leaseDuration,
            cancellationToken
        );

        if (lease is null)
            return BuildNak(request);

        Dhcpv4Message ack = NewReply(request, IPAddress.Parse(lease.IpAddress));
        ack.SetMessageType(Dhcpv4MessageTypeEnum.Ack);
        Dhcpv4OptionDeliveryService.Apply(
            ack,
            scope,
            request,
            serverIdentifierProvider.Value,
            leaseDuration
        );
        await optionOverlayService.ApplyOverlaysAsync(ack, scope, request, cancellationToken);

        return ack;
    }

    /// <summary>
    ///     This method handles a DHCPDECLINE (RFC 2131 Section 4.3.3): the client found the
    ///     address in use, for example by ARP, and names it in the Requested IP Address option
    ///     (tag 50). The address is quarantined; there is no reply. The Server Identifier option
    ///     is not checked, so a DECLINE meant for another server is acted on too.
    /// </summary>
    private async Task<Dhcpv4Message> HandleDeclineAsync(
        Dhcpv4Message request,
        IDhcpv4ScopeResolver scopeResolver,
        IDhcpv4LeaseAllocationService leaseAllocationService,
        CancellationToken cancellationToken
    )
    {
        if (!request.TryGetRequestedIpAddress(out IPAddress declinedAddress))
            return null;

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, cancellationToken);

        if (scope is not null)
            await leaseAllocationService.DeclineAsync(
                scope,
                request,
                declinedAddress,
                cancellationToken
            );

        return null;
    }

    /// <summary>
    ///     This method handles a DHCPRELEASE (RFC 2131 Section 4.3.6): the client gives up its
    ///     lease, named by <c>ciaddr</c>, before it expires. There is no reply.
    /// </summary>
    private async Task<Dhcpv4Message> HandleReleaseAsync(
        Dhcpv4Message request,
        IDhcpv4ScopeResolver scopeResolver,
        IDhcpv4LeaseAllocationService leaseAllocationService,
        CancellationToken cancellationToken
    )
    {
        if (request.ClientIpAddress.Equals(IPAddress.Any))
            return null;

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, cancellationToken);

        if (scope is not null)
            await leaseAllocationService.ReleaseAsync(
                scope,
                request,
                request.ClientIpAddress,
                cancellationToken
            );

        return null;
    }

    /// <summary>
    ///     This method handles a DHCPINFORM (RFC 2131 Section 4.3.5): the client already has an
    ///     address (<c>ciaddr</c>) and asks only for configuration. The ACK carries options but no
    ///     address and no lease time.
    /// </summary>
    private async Task<Dhcpv4Message> HandleInformAsync(
        Dhcpv4Message request,
        IDhcpv4ScopeResolver scopeResolver,
        IDhcpv4OptionOverlayService optionOverlayService,
        CancellationToken cancellationToken
    )
    {
        if (request.ClientIpAddress.Equals(IPAddress.Any))
            return null;

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, cancellationToken);

        if (scope is null)
            return null;

        Dhcpv4Message ack = NewReply(request, IPAddress.Any);
        ack.ClientIpAddress = request.ClientIpAddress;
        ack.SetMessageType(Dhcpv4MessageTypeEnum.Ack);
        Dhcpv4OptionDeliveryService.Apply(
            ack,
            scope,
            request,
            serverIdentifierProvider.Value,
            leaseDuration: null
        );
        await optionOverlayService.ApplyOverlaysAsync(ack, scope, request, cancellationToken);

        return ack;
    }

    /// <summary>
    ///     This method builds the fields every reply copies from the request, as RFC 2131
    ///     Section 4.3 expects: the transaction identifier, hardware address fields, <c>giaddr</c>
    ///     and the broadcast flag.
    /// </summary>
    private static Dhcpv4Message NewReply(Dhcpv4Message request, IPAddress yourIpAddress) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootReply,
            TransactionId = request.TransactionId,
            YourIpAddress = yourIpAddress,
            RelayAgentIpAddress = request.RelayAgentIpAddress,
            BroadcastFlagSet = request.BroadcastFlagSet,
            ClientHardwareAddress = request.ClientHardwareAddress,
            HardwareAddressType = request.HardwareAddressType,
            HardwareAddressLength = request.HardwareAddressLength,
        };

    /// <summary>
    ///     This method builds a DHCPNAK (RFC 2131 Section 4.3.2), which carries only the message
    ///     type and the Server Identifier.
    /// </summary>
    private Dhcpv4Message BuildNak(Dhcpv4Message request)
    {
        Dhcpv4Message nak = NewReply(request, IPAddress.Any);
        nak.SetMessageType(Dhcpv4MessageTypeEnum.Nak);
        nak.SetServerIdentifier(serverIdentifierProvider.Value);
        return nak;
    }
}

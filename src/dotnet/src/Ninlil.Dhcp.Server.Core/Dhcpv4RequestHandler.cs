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
///     This class is the real DHCPv4 server engine: the single <see cref="IDhcpv4RequestHandler" />
///     implementation <see cref="Dhcpv4UdpListener" /> dispatches every decoded request to. It
///     implements RFC 2131 Section 4.3's full message-type state machine --
///     DISCOVER-Offer-Request-Ack ("DORA"), plus DECLINE/RELEASE/INFORM -- entirely by delegating
///     to <see cref="IDhcpv4ScopeResolver" /> (which scope a request belongs to),
///     <see cref="IDhcpv4LeaseAllocationService" /> (lease allocation/confirmation/decline/
///     release), <see cref="Dhcpv4OptionDeliveryService" /> (standard option delivery), and
///     <see cref="IDhcpv4OptionOverlayService" /> (a scope's own custom options and any matching
///     tag rules, applied strictly after the standard options); this class itself owns only the
///     message-type dispatch and reply-construction logic RFC 2131 Section 4.3 describes.
///     <para>
///         <b>Why this class creates its own dependency injection scope per request</b>:
///         <see cref="Dhcpv4UdpListener" /> is a <see cref="Microsoft.Extensions.Hosting.BackgroundService" />
///         -- a singleton for the whole lifetime of the process -- and resolves this class's own
///         registered singleton instance exactly once, at startup. <see cref="IDhcpv4ScopeResolver" />
///         and <see cref="IDhcpv4LeaseAllocationService" />, on the other hand, are registered
///         scoped (they ultimately depend on Entity Framework Core's own
///         <c>DbContext</c>-backed repositories through <c>IUnitOfWork</c>, which is never safe to
///         share as a single, process-lifetime instance across concurrent requests). Resolving a
///         scoped service directly from a singleton's own constructor would either fail outright
///         (with scope validation enabled) or silently create a real captive-dependency bug --
///         reusing the same <c>DbContext</c> for the life of the process across every concurrent
///         DHCP request, whose tracked entities would then go stale and conflict across requests.
///         This class instead takes only an
///         <see cref="IServiceScopeFactory" /> and creates a fresh, short-lived scope for every
///         single <see cref="HandleRequestAsync" /> call, mirroring
///         <c>Ninlil.Dns.Server.Authoritative.Cache.AuthoritativeZoneCache</c>'s own
///         established "singleton service, scoped work" shape exactly.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     Used to create a fresh dependency injection scope for every request, so this handler's own
///     scoped dependencies are never captured by its own singleton lifetime.
/// </param>
/// <param name="serverIdentifierProvider">This server's own Server Identifier address.</param>
/// <param name="logger">The logger this handler's own dispatch decisions are recorded to.</param>
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

        // A fresh scope per request, per this class's own doc comment -- everything below this
        // point resolves its own scoped dependencies from it rather than from any field captured
        // at this singleton's own construction time.
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

            // OFFER/ACK/NAK are server-originated message types; a client (or relay agent)
            // sending one this server ever receives is either malformed or malicious, and either
            // way there is nothing sensible to answer.
            _ => null,
        };
    }

    /// <summary>
    ///     This method handles a DHCPDISCOVER, per RFC 2131 Section 4.3.1: resolve the client's
    ///     scope, tentatively allocate an address for it, and offer it back.
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
    ///     This method handles a DHCPREQUEST, classifying it into one of RFC 2131 Section 4.3.2's
    ///     three scenarios (selecting, init-reboot, or renewing/rebinding) based on which of the
    ///     Server Identifier and Requested IP Address options it carries, and whether it already
    ///     has a usable client address (<c>ciaddr</c>).
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
            // SELECTING: the client is accepting a specific server's OFFER. If it selected a
            // different server, this request is not addressed to us at all -- remain completely
            // silent, per RFC 2131 Section 4.3.2.
            if (!requestedServer.Equals(serverIdentifierProvider.Value))
                return null;

            if (!hasRequestedAddress)
                return null;

            addressToConfirm = requestedAddress;
        }
        else if (hasClientAddress)
        {
            // RENEWING or REBINDING: the client already believes it holds ciaddr and is asking to
            // extend that lease.
            addressToConfirm = request.ClientIpAddress;
        }
        else if (hasRequestedAddress)
        {
            // INIT-REBOOT: the client remembers a previous lease and is verifying it is still
            // valid after rebooting, with neither ciaddr nor a server identifier set.
            addressToConfirm = requestedAddress;
        }
        else
        {
            // None of the three recognizable shapes RFC 2131 describes -- nothing sensible to do.
            logger.LogDebug(
                "Ignoring a malformed DHCPREQUEST that carries neither a Server Identifier, a client address, nor a Requested IP Address option."
            );
            return null;
        }

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, cancellationToken);

        if (scope is null)
            // This server has no record of this client and no scope whose subnet could plausibly
            // contain it -- RFC 2131 Section 4.3.2 calls for silence, not a NAK, when a server has
            // no authority over the client's own network at all.
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
    ///     This method handles a DHCPDECLINE, per RFC 2131 Section 4.3.3: the client reports that
    ///     the address it was offered is already in use by something else on the network (found
    ///     via its own ARP probe). The address's own Requested IP Address option (tag 50) names
    ///     which one; there is no reply to a DECLINE.
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
    ///     This method handles a DHCPRELEASE, per RFC 2131 Section 4.3.6: the client is
    ///     voluntarily giving up its lease before it expires. The address's own <c>ciaddr</c> names
    ///     which one; there is no reply to a RELEASE.
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
    ///     This method handles a DHCPINFORM, per RFC 2131 Section 4.3.5: the client already has an
    ///     address through some other means and is purely asking for additional local
    ///     configuration parameters. No address is allocated and the reply carries no lease time.
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
    ///     This method builds the common shape every reply this handler ever sends shares: the
    ///     transaction identifier, hardware-address fields, and relay/broadcast echoes RFC 2131
    ///     Section 4.3 expects a server to carry through unchanged from the original request.
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
    ///     This method builds a DHCPNAK, per RFC 2131 Section 4.3.2: carrying nothing beyond the
    ///     message type and this server's own Server Identifier.
    /// </summary>
    private Dhcpv4Message BuildNak(Dhcpv4Message request)
    {
        Dhcpv4Message nak = NewReply(request, IPAddress.Any);
        nak.SetMessageType(Dhcpv4MessageTypeEnum.Nak);
        nak.SetServerIdentifier(serverIdentifierProvider.Value);
        return nak;
    }
}

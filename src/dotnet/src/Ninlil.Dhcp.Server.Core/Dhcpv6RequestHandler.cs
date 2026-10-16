using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.DnsUpdate;
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dhcp.Server.Core.Options;
using Ninlil.Dhcp.Server.Core.Scope;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core;

/// <summary>
///     This class is the DHCPv6 server engine, the <see cref="IDhcpv6RequestHandler" /> that
///     <see cref="Dhcpv6UdpListener" /> passes every decoded client message to, and the
///     counterpart of <see cref="Dhcpv4RequestHandler" />. Like that class it is a singleton that
///     creates a dependency injection scope per message for its scoped services.
///     <para>
///         <b>Messages.</b> SOLICIT (with optional Rapid Commit), REQUEST, RENEW, REBIND, RELEASE,
///         DECLINE, CONFIRM and INFORMATION-REQUEST are handled. REQUEST, RENEW and REBIND share
///         <see cref="HandleConfirmingExchangeAsync" />, since each commits an address or prefix
///         the client names; REQUEST and RENEW must name this server, REBIND need not (RFC 8415
///         Section 18.2.5). Server message types get no answer. RELAY-FORW arrives through
///         <see cref="HandleRelayMessageAsync" />.
///     </para>
///     <para>
///         <b>Not handled.</b> IA_TA (temporary addresses) is ignored, and so is every IA after
///         the first IA_NA and IA_PD (see <see cref="IDhcpv6LeaseAllocationService" />); only a
///         DECLINE processes every IA_NA.
///         <b>Options.</b> <see cref="Dhcpv6OptionDeliveryService" /> adds the standard options,
///         then <see cref="IDhcpv6OptionOverlayService" /> adds the scope's custom options
///         (<see cref="DhcpScopeCustomOptionEntity" />) and the tag rules to the replies that
///         carry configuration: SOLICIT, REQUEST/RENEW/REBIND and INFORMATION-REQUEST, as in
///         DHCPv4. RELEASE, DECLINE and CONFIRM replies carry only status. A REQUEST, RENEW or
///         REBIND for an address or prefix that cannot be confirmed always gets
///         <c>NoBinding</c>, never <c>NoAddrsAvail</c> or <c>NoPrefixAvail</c>; either way the
///         client starts again with a SOLICIT. A SOLICIT on a link with a scope but nothing left
///         to offer gets <c>NoAddrsAvail</c> or <c>NoPrefixAvail</c> in its IAs; on a link with
///         no scope it gets no reply.
///     </para>
///     <para>
///         <b>Relays.</b> As the <see cref="IDhcpv6RelayMessageHandler" />,
///         <see cref="HandleRelayMessageAsync" /> unwraps a RELAY-FORW from an external relay
///         agent, handles the client message with the same <see cref="ProcessMessageAsync" /> as
///         an unrelayed one, and wraps the reply in a RELAY-REPL that copies the hop count, link
///         address and peer address and echoes the Interface-ID.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     Used to create a scope for every message (see the class summary).
/// </param>
/// <param name="serverIdentifierProvider">The server's DUID.</param>
/// <param name="options">The server engine configuration, read for Rapid Commit.</param>
/// <param name="logger">The logger ignored messages are recorded to.</param>
/// <param name="timeProvider">
///     The clock for the rate limit on refusal warnings; <see cref="TimeProvider.System" /> when
///     <see langword="null" />.
/// </param>
public sealed class Dhcpv6RequestHandler(
    IServiceScopeFactory scopeFactory,
    IDhcpv6ServerIdentifierProvider serverIdentifierProvider,
    IOptions<DhcpServerOptions> options,
    ILogger<Dhcpv6RequestHandler> logger,
    TimeProvider timeProvider = null
) : IDhcpv6RequestHandler, IDhcpv6RelayMessageHandler
{
    private readonly LongClientIdentifierLimiter _longIdentifierLimiter = new(
        timeProvider ?? TimeProvider.System
    );

    /// <summary>
    ///     This type holds what is decoded from a request's RFC 4704 Client FQDN option (code 39),
    ///     once, so it can supply the <c>hostName</c> of
    ///     <see cref="Ninlil.Dhcp.Server.Core.Allocation.IDhcpv6LeaseAllocationService.ConfirmAddressAsync" />
    ///     and then build the reply option.
    /// </summary>
    /// <param name="HadOption">
    ///     Whether the request carried a Client FQDN option. When it did not, the reply carries
    ///     none either, since RFC 4704 is opt-in for the client.
    /// </param>
    /// <param name="ClientRequestedServerAaaaUpdate">
    ///     The client's "S" bit, used only to set the reply's "O" (override) bit: RFC 4704 Section
    ///     4.1 says "A server MUST set this bit to 1 if the 'S' bit in its reply to the client
    ///     does not match the 'S' bit received from the client."
    /// </param>
    /// <param name="DomainName">
    ///     The client's domain name, copied unchanged into the reply, which RFC 4704 Section 6
    ///     allows ("The server MAY simply copy the Domain Name field from the Client FQDN option
    ///     that the client sent to the server."). The name that is registered is built later,
    ///     asynchronously, by <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>;
    ///     building it here too could make the two disagree.
    /// </param>
    /// <param name="IsFullyQualified">Whether the client's name was fully qualified; copied into the reply with <see cref="DomainName" />.</param>
    /// <param name="HostNameCandidate">
    ///     The label passed to
    ///     <see cref="Ninlil.Dhcp.Server.Core.Allocation.IDhcpv6LeaseAllocationService.ConfirmAddressAsync" />
    ///     as the lease's host name: the first label of <see cref="DomainName" />. It is
    ///     <see langword="null" /> when the Domain Name field was empty (RFC 4704 Section 4.2 lets
    ///     a client ask the server to provide a name; this server never makes one up, as with
    ///     DHCPv4 without a Host Name option) or when the client set the "N" bit, which the
    ///     server does not override (see <see cref="DecodeClientFqdn" />).
    /// </param>
    private readonly record struct ClientFqdnRequest(
        bool HadOption,
        bool ClientRequestedServerAaaaUpdate,
        string DomainName,
        bool IsFullyQualified,
        string HostNameCandidate
    );

    /// <summary>
    ///     This constant is the highest RELAY-FORW hop count (RFC 8415 Section 7.1 "hop-count")
    ///     that is still processed, a guard against relay loops and malformed input like
    ///     <c>Ninlil.Dhcp.Server.Relay.Dhcpv4RelayAgent.MaxHops</c>. A single-hop chain (see
    ///     <see cref="Dhcpv6RelayMessage" />) arrives with hop count 0.
    /// </summary>
    private const byte MaxRelayHopCount = 16;

    /// <inheritdoc />
    public ValueTask<Dhcpv6Message> HandleRequestAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    ) => new(ProcessMessageAsync(request, remoteEndPoint, cancellationToken));

    /// <inheritdoc />
    public async Task<Dhcpv6RelayMessage> HandleRelayMessageAsync(
        Dhcpv6RelayMessage message,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        // A RELAY-REPL here is another server's reply sent to the wrong place; ignore it.
        if (message.MessageType != Dhcpv6MessageTypeEnum.RelayForward)
        {
            logger.LogDebug(
                "Ignoring a {MessageType} from {RemoteEndPoint}; this server engine only ever processes a RELAY-FORW.",
                message.MessageType,
                remoteEndPoint
            );
            return null;
        }

        if (message.HopCount > MaxRelayHopCount)
        {
            logger.LogWarning(
                "Discarding a RELAY-FORW from {RemoteEndPoint} whose own hop-count ({HopCount}) exceeds this server's own defensive limit ({MaxHops}).",
                remoteEndPoint,
                message.HopCount,
                MaxRelayHopCount
            );
            return null;
        }

        if (!message.TryGetRelayMessagePayload(out byte[] innerPayload))
        {
            logger.LogWarning(
                "Discarding a RELAY-FORW from {RemoteEndPoint} carrying no Relay Message option.",
                remoteEndPoint
            );
            return null;
        }

        if (
            !Dhcpv6MessageReader.TryRead(
                innerPayload,
                out Dhcpv6Message innerRequest,
                out string failureReason
            )
        )
        {
            logger.LogWarning(
                "Discarding a RELAY-FORW from {RemoteEndPoint} whose own nested message could not be decoded: {Reason}",
                remoteEndPoint,
                failureReason
            );
            return null;
        }

        // The link-address names the client's link (the UDP source is the relay agent), and
        // Dhcpv6ScopeResolver uses it to pick the scope.
        innerRequest.RelayLinkAddress = message.LinkAddress;

        // Copied onto the inner message, like RelayLinkAddress, for tag rules
        // (Dhcpv6OptionOverlayService) to match; neither is part of the client message itself.
        if (message.TryGetInterfaceId(out byte[] requestInterfaceId))
            innerRequest.RelayInterfaceId = requestInterfaceId;

        if (message.TryGetRemoteId(out byte[] requestRemoteId))
            innerRequest.RelayRemoteId = requestRemoteId;

        Dhcpv6Message innerReply = await ProcessMessageAsync(
            innerRequest,
            remoteEndPoint,
            cancellationToken
        );

        if (innerReply is null)
            return null;

        Dhcpv6RelayMessage relayReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayReply,
            // A RELAY-REPL copies hop-count, link-address and peer-address from the RELAY-FORW
            // it answers (RFC 8415).
            HopCount = message.HopCount,
            LinkAddress = message.LinkAddress,
            PeerAddress = message.PeerAddress,
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerReply));

        // RFC 8415 Section 21.18: echo the relay agent's Interface-ID unchanged.
        if (message.TryGetInterfaceId(out byte[] interfaceId))
            relayReply.SetInterfaceId(interfaceId);

        return relayReply;
    }

    /// <summary>
    ///     This method handles one client message for both <see cref="HandleRequestAsync" /> and
    ///     <see cref="HandleRelayMessageAsync" />; the only difference is whether the relay fields
    ///     (<see cref="Dhcpv6Message.RelayLinkAddress" /> and others) are set. A message without a
    ///     Client Identifier is ignored.
    /// </summary>
    private async Task<Dhcpv6Message> ProcessMessageAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        // RFC 8415 requires a Client Identifier on the client messages handled here, and without
        // one no lease can be found or named.
        byte[] clientDuid = Dhcpv6ClientIdentifier.Resolve(request);

        // A DUID over 85 bytes cannot be stored (the column and index size), so the message gets
        // no reply and nothing is allocated or saved.
        if (LongClientIdentifierLimiter.IsTooLong(clientDuid))
        {
            _longIdentifierLimiter.Refuse(logger, "DHCPv6", remoteEndPoint, clientDuid.Length);
            return null;
        }

        string clientKey = Dhcpv6ClientIdentifier.Format(request);

        if (clientKey is null)
        {
            logger.LogDebug(
                "Ignoring a DHCPv6 message from {RemoteEndPoint} with no Client Identifier option.",
                remoteEndPoint
            );
            return null;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDhcpv6ScopeResolver scopeResolver =
            scope.ServiceProvider.GetRequiredService<IDhcpv6ScopeResolver>();
        IDhcpv6LeaseAllocationService leaseAllocationService =
            scope.ServiceProvider.GetRequiredService<IDhcpv6LeaseAllocationService>();
        IDhcpv6OptionOverlayService optionOverlayService =
            scope.ServiceProvider.GetRequiredService<IDhcpv6OptionOverlayService>();
        IDhcpDnsUpdatePolicy dnsUpdatePolicy =
            scope.ServiceProvider.GetRequiredService<IDhcpDnsUpdatePolicy>();

        return request.MessageType switch
        {
            Dhcpv6MessageTypeEnum.Solicit => await HandleSolicitAsync(
                request,
                clientKey,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                dnsUpdatePolicy,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Request => await HandleConfirmingExchangeAsync(
                request,
                clientKey,
                requireServerIdentifierMatch: true,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                dnsUpdatePolicy,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Renew => await HandleConfirmingExchangeAsync(
                request,
                clientKey,
                requireServerIdentifierMatch: true,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                dnsUpdatePolicy,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Rebind => await HandleConfirmingExchangeAsync(
                request,
                clientKey,
                requireServerIdentifierMatch: false,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                dnsUpdatePolicy,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Release => await HandleReleaseAsync(
                request,
                clientKey,
                scopeResolver,
                leaseAllocationService,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Decline => await HandleDeclineAsync(
                request,
                clientKey,
                scopeResolver,
                leaseAllocationService,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Confirm => await HandleConfirmAsync(
                request,
                scopeResolver,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.InformationRequest => await HandleInformationRequestAsync(
                request,
                scopeResolver,
                optionOverlayService,
                cancellationToken
            ),

            // ADVERTISE, REPLY and RECONFIGURE come from servers; one received here gets no
            // answer.
            _ => null,
        };
    }

    /// <summary>
    ///     This method handles a SOLICIT: it resolves a scope for the first IA_NA and IA_PD, offers
    ///     an address or prefix for each, and returns an ADVERTISE (RFC 8415 Section 18.3.9). When
    ///     the client asks for Rapid Commit and
    ///     <see cref="DhcpServerOptions.EnableDhcpv6RapidCommit" /> is on, it commits them at once
    ///     and returns a REPLY. When a scope serves the client's link but nothing can be offered,
    ///     the reply still goes out, with <c>NoAddrsAvail</c> in each IA_NA and
    ///     <c>NoPrefixAvail</c> in each IA_PD that got nothing (RFC 8415 Section 18.3.9: the
    ///     server "MUST include the IA option in the Advertise message with no addresses in that
    ///     IA and a Status Code option ... containing status code NoAddrsAvail"). The RFC asks for
    ///     no top-level Status Code, so none is added. With Rapid Commit the same holds for the
    ///     REPLY: Section 18.3.1 has the server produce it "as though it had received a Request
    ///     message", and Section 18.3.2 gives the same per-IA statuses. When no scope exists for
    ///     the link there is no reply, because this server has no configuration for the client
    ///     and another server may serve it.
    /// </summary>
    private async Task<Dhcpv6Message> HandleSolicitAsync(
        Dhcpv6Message request,
        string clientKey,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        IDhcpv6OptionOverlayService optionOverlayService,
        IDhcpDnsUpdatePolicy dnsUpdatePolicy,
        CancellationToken cancellationToken
    )
    {
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestedNa = FirstOrDefaultNa(request);
        Dhcpv6IdentityAssociationForPrefixDelegation requestedPd = FirstOrDefaultPd(request);

        if (requestedNa is null && requestedPd is null)
            return null;

        bool rapidCommit = request.TryGetRapidCommit() && options.Value.EnableDhcpv6RapidCommit;

        Dhcpv6Message reply = NewReply(
            request,
            rapidCommit ? Dhcpv6MessageTypeEnum.Reply : Dhcpv6MessageTypeEnum.Advertise
        );

        bool offeredAnything = false;

        // True once an IA found a scope, whether or not it got an address or prefix: only then
        // does this server speak for the client's link.
        bool scopeFound = false;
        DhcpScopeEntity optionScope = null;

        // Only Rapid Commit commits an address in this exchange, so only then is the Client FQDN
        // option processed; an ADVERTISE has nothing to report yet.
        ClientFqdnRequest clientFqdn = default;

        if (requestedNa is not null)
        {
            if (rapidCommit)
                clientFqdn = DecodeClientFqdn(request);

            (DhcpScopeEntity scope, Dhcpv6IdentityAssociationForNonTemporaryAddresses replyNa) =
                rapidCommit
                    ? await RapidCommitAddressAssociationAsync(
                        scopeResolver,
                        leaseAllocationService,
                        request,
                        clientKey,
                        requestedNa,
                        clientFqdn.HostNameCandidate,
                        cancellationToken
                    )
                    : await OfferAddressAssociationAsync(
                        scopeResolver,
                        leaseAllocationService,
                        request,
                        clientKey,
                        requestedNa,
                        cancellationToken
                    );

            if (replyNa is not null)
            {
                reply.AddIdentityAssociationForNonTemporaryAddresses(replyNa);
                scopeFound = true;

                // An association is added whenever a scope was found, carrying a Status Code when
                // nothing could be allocated; only one with an address counts as an offer, which
                // decides whether the scope's options are added below.
                if (replyNa.Addresses.Count > 0)
                {
                    offeredAnything = true;
                    optionScope ??= scope;
                }

                if (rapidCommit)
                    await ApplyClientFqdnReplyAsync(
                        reply,
                        clientFqdn,
                        replyNa.Addresses.Count > 0,
                        scope,
                        dnsUpdatePolicy,
                        cancellationToken
                    );
            }
        }

        if (requestedPd is not null)
        {
            (DhcpScopeEntity scope, Dhcpv6IdentityAssociationForPrefixDelegation replyPd) =
                rapidCommit
                    ? await RapidCommitPrefixAssociationAsync(
                        scopeResolver,
                        leaseAllocationService,
                        request,
                        clientKey,
                        requestedPd,
                        cancellationToken
                    )
                    : await OfferPrefixAssociationAsync(
                        scopeResolver,
                        leaseAllocationService,
                        request,
                        clientKey,
                        requestedPd,
                        cancellationToken
                    );

            if (replyPd is not null)
            {
                reply.AddIdentityAssociationForPrefixDelegation(replyPd);
                scopeFound = true;

                // Only an association with a prefix counts as an offer, as for IA_NA above.
                if (replyPd.Prefixes.Count > 0)
                {
                    offeredAnything = true;
                    optionScope ??= scope;
                }
            }
        }

        // No IA resolved a scope: this server has no configuration for the client's link, so it
        // stays silent and lets another server answer.
        if (!scopeFound)
            return null;

        if (rapidCommit)
            reply.SetRapidCommit();

        // A scope exists but nothing was offered: the reply carries the per-IA statuses and the
        // identifiers only. The scope's options and the overlays would describe a configuration
        // the client cannot use, so they are left out.
        if (!offeredAnything)
        {
            Dhcpv6OptionDeliveryService.Apply(
                reply,
                scope: null,
                request,
                serverIdentifierProvider.Value
            );
            return reply;
        }

        Dhcpv6OptionDeliveryService.Apply(
            reply,
            optionScope,
            request,
            serverIdentifierProvider.Value
        );
        await optionOverlayService.ApplyOverlaysAsync(
            reply,
            optionScope,
            request,
            cancellationToken
        );
        return reply;
    }

    /// <summary>
    ///     This method handles REQUEST, RENEW and REBIND (RFC 8415 Sections 18.3.2, 18.3.4 and
    ///     18.3.5; see the class summary). It commits the address and prefix the client names in
    ///     its first IA_NA and IA_PD and always replies, with a Status Code in any IA that could
    ///     not be confirmed.
    /// </summary>
    private async Task<Dhcpv6Message> HandleConfirmingExchangeAsync(
        Dhcpv6Message request,
        string clientKey,
        bool requireServerIdentifierMatch,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        IDhcpv6OptionOverlayService optionOverlayService,
        IDhcpDnsUpdatePolicy dnsUpdatePolicy,
        CancellationToken cancellationToken
    )
    {
        if (requireServerIdentifierMatch && !HasOurServerIdentifier(request))
            // No Server Identifier, or another server's: not for us, so stay silent (RFC 8415).
            return null;

        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestedNa = FirstOrDefaultNa(request);
        Dhcpv6IdentityAssociationForPrefixDelegation requestedPd = FirstOrDefaultPd(request);

        if (requestedNa is null && requestedPd is null)
            return null;

        Dhcpv6Message reply = NewReply(request, Dhcpv6MessageTypeEnum.Reply);
        DhcpScopeEntity optionScope = null;

        if (requestedNa is not null)
        {
            ClientFqdnRequest clientFqdn = DecodeClientFqdn(request);

            (DhcpScopeEntity scope, Dhcpv6IdentityAssociationForNonTemporaryAddresses replyNa) =
                await CommitAddressAssociationAsync(
                    scopeResolver,
                    leaseAllocationService,
                    request,
                    clientKey,
                    requestedNa,
                    clientFqdn.HostNameCandidate,
                    cancellationToken
                );
            reply.AddIdentityAssociationForNonTemporaryAddresses(replyNa);
            optionScope ??= scope;
            await ApplyClientFqdnReplyAsync(
                reply,
                clientFqdn,
                replyNa.Addresses.Count > 0,
                scope,
                dnsUpdatePolicy,
                cancellationToken
            );
        }

        if (requestedPd is not null)
        {
            (DhcpScopeEntity scope, Dhcpv6IdentityAssociationForPrefixDelegation replyPd) =
                await CommitPrefixAssociationAsync(
                    scopeResolver,
                    leaseAllocationService,
                    request,
                    clientKey,
                    requestedPd,
                    cancellationToken
                );
            reply.AddIdentityAssociationForPrefixDelegation(replyPd);
            optionScope ??= scope;
        }

        Dhcpv6OptionDeliveryService.Apply(
            reply,
            optionScope,
            request,
            serverIdentifierProvider.Value
        );
        await optionOverlayService.ApplyOverlaysAsync(
            reply,
            optionScope,
            request,
            cancellationToken
        );
        return reply;
    }

    /// <summary>
    ///     This method tells whether a message carries this server's Server Identifier. A missing
    ///     one does not match.
    /// </summary>
    private bool HasOurServerIdentifier(Dhcpv6Message request) =>
        request.TryGetServerIdentifier(out byte[] requestedServerDuid)
        && requestedServerDuid.AsSpan().SequenceEqual(serverIdentifierProvider.Value);

    /// <summary>
    ///     This method handles a RELEASE (RFC 8415). Unlike DHCPv4, a DHCPv6 server replies, with
    ///     <c>Success</c> or <c>NoBinding</c> per IA. Only the first address or prefix of each IA is
    ///     released. A RELEASE without this server's Server Identifier gets no reply (RFC 8415
    ///     Section 16.9).
    /// </summary>
    private async Task<Dhcpv6Message> HandleReleaseAsync(
        Dhcpv6Message request,
        string clientKey,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        CancellationToken cancellationToken
    )
    {
        if (!HasOurServerIdentifier(request))
            return null;

        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestedNa = FirstOrDefaultNa(request);
        Dhcpv6IdentityAssociationForPrefixDelegation requestedPd = FirstOrDefaultPd(request);

        if (requestedNa is null && requestedPd is null)
            return null;

        Dhcpv6Message reply = NewReply(request, Dhcpv6MessageTypeEnum.Reply);

        if (requestedNa is not null)
        {
            IPAddress address = requestedNa.Addresses.FirstOrDefault()?.Address;
            bool released =
                address is not null
                && await ReleaseWithinResolvedScopeAsync(
                    scopeResolver,
                    leaseAllocationService,
                    request,
                    clientKey,
                    DhcpScopeKindEnum.AddressAssignment,
                    address,
                    prefixLength: null,
                    cancellationToken
                );

            reply.AddIdentityAssociationForNonTemporaryAddresses(
                BuildNaStatus(
                    requestedNa.IdentityAssociationId,
                    released ? Dhcpv6StatusCodeEnum.Success : Dhcpv6StatusCodeEnum.NoBinding
                )
            );
        }

        if (requestedPd is not null)
        {
            Dhcpv6IdentityAssociationPrefix prefixHint = requestedPd.Prefixes.FirstOrDefault();
            bool released =
                prefixHint is not null
                && await ReleaseWithinResolvedScopeAsync(
                    scopeResolver,
                    leaseAllocationService,
                    request,
                    clientKey,
                    DhcpScopeKindEnum.PrefixDelegation,
                    prefixHint.Prefix,
                    prefixHint.PrefixLength,
                    cancellationToken
                );

            reply.AddIdentityAssociationForPrefixDelegation(
                BuildPdStatus(
                    requestedPd.IdentityAssociationId,
                    released ? Dhcpv6StatusCodeEnum.Success : Dhcpv6StatusCodeEnum.NoBinding
                )
            );
        }

        Dhcpv6OptionDeliveryService.Apply(
            reply,
            scope: null,
            request,
            serverIdentifierProvider.Value
        );
        return reply;
    }

    /// <summary>
    ///     This method handles a DECLINE (RFC 8415): the client found addresses in its IA_NAs
    ///     already in use by duplicate address detection, and each address that is the client's
    ///     own lease is quarantined. Every IA_NA is processed. Unlike DHCPv4, the server replies
    ///     with one IA per requested IA_NA: <c>Success</c> when at least one of its addresses was
    ///     the client's, otherwise <c>NoBinding</c> (Section 18.3.8: "For each IA in the Decline
    ///     message for which the server has no binding information, the server adds an IA option
    ///     ... with the value NoBinding").
    ///     Prefixes cannot be declined. A DECLINE without this server's Server Identifier gets no
    ///     reply (Section 16.8).
    /// </summary>
    private async Task<Dhcpv6Message> HandleDeclineAsync(
        Dhcpv6Message request,
        string clientKey,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        CancellationToken cancellationToken
    )
    {
        if (!HasOurServerIdentifier(request))
            return null;

        if (
            !request.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> requestedNas
            )
        )
            return null;

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );

        Dhcpv6Message reply = NewReply(request, Dhcpv6MessageTypeEnum.Reply);

        foreach (Dhcpv6IdentityAssociationForNonTemporaryAddresses requestedNa in requestedNas)
        {
            bool declinedAny = false;

            if (scope is not null)
                foreach (Dhcpv6IdentityAssociationAddress declined in requestedNa.Addresses)
                    declinedAny |= await leaseAllocationService.DeclineAddressAsync(
                        scope,
                        clientKey,
                        declined.Address,
                        cancellationToken
                    );

            reply.AddIdentityAssociationForNonTemporaryAddresses(
                BuildNaStatus(
                    requestedNa.IdentityAssociationId,
                    declinedAny ? Dhcpv6StatusCodeEnum.Success : Dhcpv6StatusCodeEnum.NoBinding
                )
            );
        }

        Dhcpv6OptionDeliveryService.Apply(reply, scope, request, serverIdentifierProvider.Value);
        return reply;
    }

    /// <summary>
    ///     This method handles a CONFIRM (RFC 8415 Section 18.3.3). Nothing is allocated: it checks
    ///     whether every address in every IA_NA is on the link of the resolved address-assignment
    ///     scope, judged by the scope's subnet (<c>SubnetPrefixLength</c> around
    ///     <c>RangeStartAddress</c>), and replies <c>NotOnLink</c> if any is not. With no scope it
    ///     has nothing to judge by and stays silent, as RFC 8415 asks.
    /// </summary>
    private async Task<Dhcpv6Message> HandleConfirmAsync(
        Dhcpv6Message request,
        IDhcpv6ScopeResolver scopeResolver,
        CancellationToken cancellationToken
    )
    {
        if (
            !request.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
            )
        )
            return null;

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );

        if (scope is null)
            return null;

        bool allOnLink = true;

        foreach (Dhcpv6IdentityAssociationForNonTemporaryAddresses association in associations)
        foreach (Dhcpv6IdentityAssociationAddress address in association.Addresses)
        {
            if (
                !IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
                || !Dhcpv6NetworkHelper.AreInSameNetwork(
                    rangeStart,
                    address.Address,
                    scope.SubnetPrefixLength
                )
            )
            {
                allOnLink = false;
                break;
            }
        }

        Dhcpv6Message reply = NewReply(request, Dhcpv6MessageTypeEnum.Reply);

        if (!allOnLink)
            reply.SetStatusCode(Dhcpv6StatusCodeEnum.NotOnLink);

        Dhcpv6OptionDeliveryService.Apply(reply, scope, request, serverIdentifierProvider.Value);
        return reply;
    }

    /// <summary>
    ///     This method handles an INFORMATION-REQUEST (RFC 8415): the client has an address and asks
    ///     only for configuration (DNS servers and tag-rule options). IAs are not processed, since
    ///     a client must not include one. It always replies, even without a scope.
    /// </summary>
    private async Task<Dhcpv6Message> HandleInformationRequestAsync(
        Dhcpv6Message request,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6OptionOverlayService optionOverlayService,
        CancellationToken cancellationToken
    )
    {
        // The address-assignment resolver is used for its relay and single-scope steps; a client
        // with no lease or reservation simply skips the first two.
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );

        Dhcpv6Message reply = NewReply(request, Dhcpv6MessageTypeEnum.Reply);
        Dhcpv6OptionDeliveryService.Apply(reply, scope, request, serverIdentifierProvider.Value);
        await optionOverlayService.ApplyOverlaysAsync(reply, scope, request, cancellationToken);
        return reply;
    }

    /// <summary>
    ///     This method resolves an address-assignment scope for one IA_NA and offers it an address,
    ///     for a SOLICIT without Rapid Commit. It returns no association when there is no scope,
    ///     and one with <c>NoAddrsAvail</c> when there is no free address.
    /// </summary>
    private static async Task<(
        DhcpScopeEntity Scope,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses Reply
    )> OfferAddressAssociationAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requested,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );

        if (scope is null)
            return (null, null);

        LeaseEntity lease = await leaseAllocationService.OfferAddressAsync(
            scope,
            clientKey,
            requested,
            cancellationToken
        );
        Dhcpv6LeaseTimes times = Dhcpv6LeaseTimeNegotiator.Negotiate(scope);

        Dhcpv6IdentityAssociationForNonTemporaryAddresses reply = new()
        {
            IdentityAssociationId = requested.IdentityAssociationId,
            T1 = times.T1,
            T2 = times.T2,
        };

        if (lease is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoAddressesAvailable));
            return (scope, reply);
        }

        reply.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = IPAddress.Parse(lease.IpAddress),
                PreferredLifetime = times.Preferred,
                ValidLifetime = times.Valid,
            }
        );

        return (scope, reply);
    }

    /// <summary>
    ///     This method resolves an address-assignment scope for one IA_NA and commits the address
    ///     the client names in it, for REQUEST, RENEW and REBIND, where the client names the
    ///     address it holds or was offered. With no scope, no address named, or an address that
    ///     cannot be confirmed, the IA carries <c>NoBinding</c>.
    /// </summary>
    /// <param name="scopeResolver">The resolver that finds the address-assignment scope.</param>
    /// <param name="leaseAllocationService">The service that commits the address.</param>
    /// <param name="request">The request being answered.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="requested">The IA_NA being committed.</param>
    /// <param name="hostName">
    ///     The host name to record for the lease, from the Client FQDN option if usable (see
    ///     <see cref="ClientFqdnRequest.HostNameCandidate" />).
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    private static async Task<(
        DhcpScopeEntity Scope,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses Reply
    )> CommitAddressAssociationAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requested,
        string hostName,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );
        IPAddress hint = requested.Addresses.FirstOrDefault()?.Address;

        Dhcpv6IdentityAssociationForNonTemporaryAddresses reply = new()
        {
            IdentityAssociationId = requested.IdentityAssociationId,
        };

        if (scope is null || hint is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoBinding));
            return (scope, reply);
        }

        Dhcpv6LeaseTimes times = Dhcpv6LeaseTimeNegotiator.Negotiate(scope);
        LeaseEntity lease = await leaseAllocationService.ConfirmAddressAsync(
            scope,
            clientKey,
            hint,
            times,
            hostName,
            cancellationToken
        );

        reply.T1 = times.T1;
        reply.T2 = times.T2;

        if (lease is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoBinding));
            return (scope, reply);
        }

        reply.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = hint,
                PreferredLifetime = times.Preferred,
                ValidLifetime = times.Valid,
            }
        );

        return (scope, reply);
    }

    /// <summary>
    ///     This method resolves an address-assignment scope for one IA_NA, picks an address as an
    ///     ordinary SOLICIT would (an IAADDR in <paramref name="requested" /> is only a hint) and
    ///     commits it at once: SOLICIT and REQUEST in one exchange. Unlike
    ///     <see cref="CommitAddressAssociationAsync" />, the client need not name an address. When
    ///     none can be offered or committed, the IA carries <c>NoAddrsAvail</c>.
    /// </summary>
    /// <param name="scopeResolver">The resolver that finds the address-assignment scope.</param>
    /// <param name="leaseAllocationService">The service that offers and then commits the address.</param>
    /// <param name="request">The request being answered.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="requested">The IA_NA being answered.</param>
    /// <param name="hostName">
    ///     The host name to record for the lease, from the Client FQDN option if usable (see
    ///     <see cref="ClientFqdnRequest.HostNameCandidate" />).
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    private static async Task<(
        DhcpScopeEntity Scope,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses Reply
    )> RapidCommitAddressAssociationAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requested,
        string hostName,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );

        Dhcpv6IdentityAssociationForNonTemporaryAddresses reply = new()
        {
            IdentityAssociationId = requested.IdentityAssociationId,
        };

        if (scope is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoAddressesAvailable));
            return (null, reply);
        }

        LeaseEntity offered = await leaseAllocationService.OfferAddressAsync(
            scope,
            clientKey,
            requested,
            cancellationToken
        );

        if (offered is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoAddressesAvailable));
            return (scope, reply);
        }

        Dhcpv6LeaseTimes times = Dhcpv6LeaseTimeNegotiator.Negotiate(scope);
        LeaseEntity committed = await leaseAllocationService.ConfirmAddressAsync(
            scope,
            clientKey,
            IPAddress.Parse(offered.IpAddress),
            times,
            hostName,
            cancellationToken
        );

        reply.T1 = times.T1;
        reply.T2 = times.T2;

        if (committed is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoAddressesAvailable));
            return (scope, reply);
        }

        reply.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = IPAddress.Parse(committed.IpAddress),
                PreferredLifetime = times.Preferred,
                ValidLifetime = times.Valid,
            }
        );

        return (scope, reply);
    }

    /// <summary>
    ///     This method resolves a prefix delegation scope for one IA_PD and offers it a prefix, for
    ///     a SOLICIT without Rapid Commit, like <see cref="OfferAddressAssociationAsync" />
    ///     (<c>NoPrefixAvail</c> when none is free).
    /// </summary>
    private static async Task<(
        DhcpScopeEntity Scope,
        Dhcpv6IdentityAssociationForPrefixDelegation Reply
    )> OfferPrefixAssociationAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        Dhcpv6IdentityAssociationForPrefixDelegation requested,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.PrefixDelegation,
            cancellationToken
        );

        if (scope is null)
            return (null, null);

        LeaseEntity lease = await leaseAllocationService.OfferPrefixAsync(
            scope,
            clientKey,
            requested,
            cancellationToken
        );
        Dhcpv6LeaseTimes times = Dhcpv6LeaseTimeNegotiator.Negotiate(scope);

        Dhcpv6IdentityAssociationForPrefixDelegation reply = new()
        {
            IdentityAssociationId = requested.IdentityAssociationId,
            T1 = times.T1,
            T2 = times.T2,
        };

        if (lease is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoPrefixAvailable));
            return (scope, reply);
        }

        reply.Prefixes.Add(
            new Dhcpv6IdentityAssociationPrefix
            {
                Prefix = IPAddress.Parse(lease.IpAddress),
                PrefixLength = (byte)(lease.PrefixLength ?? scope.DelegatedPrefixLength ?? 0),
                PreferredLifetime = times.Preferred,
                ValidLifetime = times.Valid,
            }
        );

        return (scope, reply);
    }

    /// <summary>
    ///     This method resolves a prefix delegation scope for one IA_PD and commits the prefix the
    ///     client names, for REQUEST, RENEW and REBIND, like
    ///     <see cref="CommitAddressAssociationAsync" />.
    /// </summary>
    private static async Task<(
        DhcpScopeEntity Scope,
        Dhcpv6IdentityAssociationForPrefixDelegation Reply
    )> CommitPrefixAssociationAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        Dhcpv6IdentityAssociationForPrefixDelegation requested,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.PrefixDelegation,
            cancellationToken
        );
        Dhcpv6IdentityAssociationPrefix hint = requested.Prefixes.FirstOrDefault();

        Dhcpv6IdentityAssociationForPrefixDelegation reply = new()
        {
            IdentityAssociationId = requested.IdentityAssociationId,
        };

        if (scope is null || hint is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoBinding));
            return (scope, reply);
        }

        Dhcpv6LeaseTimes times = Dhcpv6LeaseTimeNegotiator.Negotiate(scope);
        LeaseEntity lease = await leaseAllocationService.ConfirmPrefixAsync(
            scope,
            clientKey,
            hint.Prefix,
            hint.PrefixLength,
            times,
            cancellationToken
        );

        reply.T1 = times.T1;
        reply.T2 = times.T2;

        if (lease is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoBinding));
            return (scope, reply);
        }

        reply.Prefixes.Add(
            new Dhcpv6IdentityAssociationPrefix
            {
                Prefix = hint.Prefix,
                PrefixLength = hint.PrefixLength,
                PreferredLifetime = times.Preferred,
                ValidLifetime = times.Valid,
            }
        );

        return (scope, reply);
    }

    /// <summary>
    ///     This method picks a prefix for one IA_PD as an ordinary SOLICIT would and commits it at
    ///     once, like <see cref="RapidCommitAddressAssociationAsync" />.
    /// </summary>
    private static async Task<(
        DhcpScopeEntity Scope,
        Dhcpv6IdentityAssociationForPrefixDelegation Reply
    )> RapidCommitPrefixAssociationAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        Dhcpv6IdentityAssociationForPrefixDelegation requested,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.PrefixDelegation,
            cancellationToken
        );

        Dhcpv6IdentityAssociationForPrefixDelegation reply = new()
        {
            IdentityAssociationId = requested.IdentityAssociationId,
        };

        if (scope is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoPrefixAvailable));
            return (null, reply);
        }

        LeaseEntity offered = await leaseAllocationService.OfferPrefixAsync(
            scope,
            clientKey,
            requested,
            cancellationToken
        );

        if (offered is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoPrefixAvailable));
            return (scope, reply);
        }

        Dhcpv6LeaseTimes times = Dhcpv6LeaseTimeNegotiator.Negotiate(scope);
        LeaseEntity committed = await leaseAllocationService.ConfirmPrefixAsync(
            scope,
            clientKey,
            IPAddress.Parse(offered.IpAddress),
            offered.PrefixLength ?? scope.DelegatedPrefixLength ?? 0,
            times,
            cancellationToken
        );

        reply.T1 = times.T1;
        reply.T2 = times.T2;

        if (committed is null)
        {
            reply.Options.Add(StatusOption(Dhcpv6StatusCodeEnum.NoPrefixAvailable));
            return (scope, reply);
        }

        reply.Prefixes.Add(
            new Dhcpv6IdentityAssociationPrefix
            {
                Prefix = IPAddress.Parse(committed.IpAddress),
                PrefixLength = (byte)(committed.PrefixLength ?? scope.DelegatedPrefixLength ?? 0),
                PreferredLifetime = times.Preferred,
                ValidLifetime = times.Valid,
            }
        );

        return (scope, reply);
    }

    /// <summary>
    ///     This method resolves a scope of the given kind and releases the client's matching lease
    ///     in it, if there is one.
    /// </summary>
    private static async Task<bool> ReleaseWithinResolvedScopeAsync(
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        Dhcpv6Message request,
        string clientKey,
        DhcpScopeKindEnum kind,
        IPAddress value,
        int? prefixLength,
        CancellationToken cancellationToken
    )
    {
        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(request, kind, cancellationToken);

        return scope is not null
            && await leaseAllocationService.ReleaseAsync(
                scope,
                clientKey,
                value,
                prefixLength,
                cancellationToken
            );
    }

    /// <summary>
    ///     This method returns the first IA_NA of a request (only the first is handled), or
    ///     <see langword="null" />.
    /// </summary>
    private static Dhcpv6IdentityAssociationForNonTemporaryAddresses FirstOrDefaultNa(
        Dhcpv6Message request
    ) =>
        request.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
        )
            ? associations[0]
            : null;

    /// <summary>
    ///     This method returns the first IA_PD of a request (only the first is handled), or
    ///     <see langword="null" />.
    /// </summary>
    private static Dhcpv6IdentityAssociationForPrefixDelegation FirstOrDefaultPd(
        Dhcpv6Message request
    ) =>
        request.TryGetIdentityAssociationsForPrefixDelegation(
            out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
        )
            ? associations[0]
            : null;

    /// <summary>
    ///     This method builds an IA_NA holding only the IAID and a Status Code, for RELEASE and
    ///     DECLINE replies.
    /// </summary>
    private static Dhcpv6IdentityAssociationForNonTemporaryAddresses BuildNaStatus(
        uint identityAssociationId,
        Dhcpv6StatusCodeEnum statusCode
    )
    {
        Dhcpv6IdentityAssociationForNonTemporaryAddresses association = new()
        {
            IdentityAssociationId = identityAssociationId,
        };
        association.Options.Add(StatusOption(statusCode));
        return association;
    }

    /// <summary>
    ///     This method builds an IA_PD holding only the IAID and a Status Code, like
    ///     <see cref="BuildNaStatus" />.
    /// </summary>
    private static Dhcpv6IdentityAssociationForPrefixDelegation BuildPdStatus(
        uint identityAssociationId,
        Dhcpv6StatusCodeEnum statusCode
    )
    {
        Dhcpv6IdentityAssociationForPrefixDelegation association = new()
        {
            IdentityAssociationId = identityAssociationId,
        };
        association.Options.Add(StatusOption(statusCode));
        return association;
    }

    /// <summary>
    ///     This method builds a Status Code option carrying a given code and no message text.
    /// </summary>
    private static Dhcpv6Option StatusOption(Dhcpv6StatusCodeEnum statusCode) =>
        new(
            (ushort)Dhcpv6OptionCodeEnum.StatusCode,
            Dhcpv6MessageOptionExtensions.EncodeStatusCode(statusCode)
        );

    /// <summary>
    ///     This method starts a reply with the given message type and the request's transaction
    ///     identifier.
    /// </summary>
    private static Dhcpv6Message NewReply(
        Dhcpv6Message request,
        Dhcpv6MessageTypeEnum messageType
    ) => new() { MessageType = messageType, TransactionId = request.TransactionId };

    /// <summary>
    ///     This method decodes the request's RFC 4704 Client FQDN option, if any, into a
    ///     <see cref="ClientFqdnRequest" />, before the address is committed, so
    ///     <see cref="ClientFqdnRequest.HostNameCandidate" /> can be passed to
    ///     <see cref="Ninlil.Dhcp.Server.Core.Allocation.IDhcpv6LeaseAllocationService.ConfirmAddressAsync" />.
    /// </summary>
    private static ClientFqdnRequest DecodeClientFqdn(Dhcpv6Message request)
    {
        if (
            !request.TryGetClientFqdn(
                out bool serverShouldNotPerformAnyDnsUpdates,
                out _,
                out bool serverPerformsAaaaUpdate,
                out string domainName,
                out bool isFullyQualified
            )
        )
            return new ClientFqdnRequest(
                HadOption: false,
                ClientRequestedServerAaaaUpdate: false,
                DomainName: null,
                IsFullyQualified: false,
                HostNameCandidate: null
            );

        // A client that sets "N" (no updates) is honored by offering no host name; this server
        // does not override "N" as it may "S". An empty Domain Name field gives no name either.
        string hostNameCandidate = null;

        if (!serverShouldNotPerformAnyDnsUpdates && domainName is not null)
        {
            int firstDotIndex = domainName.IndexOf('.');
            hostNameCandidate = firstDotIndex >= 0 ? domainName[..firstDotIndex] : domainName;
        }

        return new ClientFqdnRequest(
            HadOption: true,
            ClientRequestedServerAaaaUpdate: serverPerformsAaaaUpdate,
            DomainName: domainName,
            IsFullyQualified: isFullyQualified,
            HostNameCandidate: hostNameCandidate
        );
    }

    /// <summary>
    ///     This method adds the Client FQDN reply option once the address commit is known, and
    ///     does nothing if the request had none (see <see cref="ClientFqdnRequest.HadOption" />).
    ///     The reply says the server performs the AAAA update (S=1, N=0) only when an address was
    ///     granted, the client offered a host name and
    ///     <see cref="IDhcpDnsUpdatePolicy.RegistersHostNamesAsync" /> says this server registers
    ///     names for the granting scope; otherwise it says the server performs none (S=0, N=1). O=1
    ///     is set whenever the reply's S differs from the client's (RFC 4704 Section 4.1). S=1 promises an
    ///     attempt, not a result: <c>DhcpDnsRegistrationService</c> can still refuse a name that
    ///     belongs to a static record or another active client, or fail the update (RFC 4704
    ///     Section 6), and the client is not told. The policy is not asked when the request had no
    ///     Client FQDN option, nor when no address or host name makes the answer false anyway.
    /// </summary>
    private static async Task ApplyClientFqdnReplyAsync(
        Dhcpv6Message reply,
        ClientFqdnRequest decoded,
        bool addressWasGranted,
        DhcpScopeEntity scope,
        IDhcpDnsUpdatePolicy dnsUpdatePolicy,
        CancellationToken cancellationToken
    )
    {
        if (!decoded.HadOption)
            return;

        bool serverPerformsAaaaUpdate =
            addressWasGranted
            && decoded.HostNameCandidate is not null
            && scope is not null
            && await dnsUpdatePolicy.RegistersHostNamesAsync(scope, cancellationToken);

        reply.SetClientFqdn(
            serverShouldNotPerformAnyDnsUpdates: !serverPerformsAaaaUpdate,
            // RFC 4704 Section 4.1: "A server MUST set this bit to 1 if the 'S' bit in its reply
            // to the client does not match the 'S' bit received from the client."
            serverOverrodeClientPreference: serverPerformsAaaaUpdate
                != decoded.ClientRequestedServerAaaaUpdate,
            serverPerformsAaaaUpdate: serverPerformsAaaaUpdate,
            domainName: decoded.DomainName,
            isFullyQualified: decoded.IsFullyQualified
        );
    }
}

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
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dhcp.Server.Core.Options;
using Ninlil.Dhcp.Server.Core.Scope;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core;

/// <summary>
///     This class is the real DHCPv6 server engine: the single <see cref="IDhcpv6RequestHandler" />
///     implementation <see cref="Dhcpv6UdpListener" /> dispatches every decoded request to --
///     the DHCPv6 analogue of <see cref="Dhcpv4RequestHandler" />, following the exact same
///     "fresh dependency injection scope per request" shape that class's own doc comment
///     documents (unchanged reasoning, not repeated here).
///     <para>
///         <b>Message-type coverage</b>: SOLICIT (with optional Rapid Commit), REQUEST, RENEW,
///         REBIND, RELEASE, DECLINE, CONFIRM, and INFORMATION-REQUEST are all implemented.
///         REQUEST/RENEW/REBIND share one private handler (<see cref="HandleConfirmingExchangeAsync" />),
///         since all three are, mechanically, "commit a specific already-known address/prefix,"
///         differing only in whether a Server Identifier match is required (REQUEST/RENEW target
///         one specific server; REBIND deliberately does not, per RFC 8415 Section 18.2.5).
///         RECONFIGURE is never received (this engine never sends one); ADVERTISE/REPLY are
///         server-originated and never legitimately arrive here; RELAY-FORW/RELAY-REPL are
///         rejected before ever reaching this class (see
///         <see cref="Dhcpv6MessageTypeEnum.RelayForward" />'s own doc comment).
///     </para>
///     <para>
///         <b>What is deliberately deferred</b>: IA_TA (temporary addresses) is not processed at
///         all -- a real, but rarely implemented even by production DHCPv6 servers, RFC 8415
///         feature, left for a future refinement rather than this already-large sub-phase. DHCPv4's
///         own scope-level custom-option overlay (<see cref="DhcpScopeCustomOptionEntity" />) has
///         no DHCPv6 analogue either -- <see cref="Dhcpv6OptionDeliveryService" /> only ever
///         applies a scope's own standard options. Tag-based <em>conditional</em> option delivery,
///         however, does now have a real DHCPv6 analogue: <see cref="IDhcpv6OptionOverlayService" />
///         applies every matching <see cref="DhcpTagRuleEntity" /> right after
///         <see cref="Dhcpv6OptionDeliveryService" /> runs, for a SOLICIT/ADVERTISE,
///         REQUEST-RENEW-REBIND/REPLY, or INFORMATION-REQUEST/REPLY exchange specifically --
///         mirroring exactly which three DHCPv4 exchanges <c>Dhcpv4OptionOverlayService</c> itself
///         applies to (DHCPv4's own DECLINE/RELEASE send no reply at all; this engine's DHCPv6
///         DECLINE/RELEASE/CONFIRM replies do, but carry only a status/binding outcome, never
///         general configuration data, so this engine does not apply tag-rule overlays to them
///         either). A REQUEST/RENEW/REBIND/Rapid-Commit-SOLICIT that names an address/prefix this server
///         cannot confirm always reports <c>NoBinding</c>, rather than distinguishing that from
///         <c>NoAddrsAvail</c>/<c>NoPrefixAvail</c> the way RFC 8415's own prose sometimes suggests
///         for a REQUEST specifically -- a deliberate simplification: every "cannot confirm" case
///         this class produces already means the same thing to a well-behaved client (fall back
///         to a fresh SOLICIT), so the two codes are not worth the extra branching to distinguish.
///     </para>
///     <para>
///         <b>Relay support</b>: this class also implements <see cref="IDhcpv6RelayMessageHandler" />,
///         the receiving side of this project's own single-relay-hop RFC 8415 Section 7/16 support
///         -- <see cref="HandleRelayMessageAsync" /> unwraps a RELAY-FORW an external relay agent
///         forwarded to this server, runs the nested client message through the exact same
///         <see cref="ProcessMessageAsync" /> logic <see cref="HandleRequestAsync" /> itself calls
///         for an unrelayed message, and wraps whatever reply comes back as a RELAY-REPL, per
///         Section 16's own rule to copy <c>hop-count</c>/<c>link-address</c>/<c>peer-address</c>
///         and echo any Interface-ID option back unchanged.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     Used to create a fresh dependency injection scope for every request -- see this class's own
///     doc comment.
/// </param>
/// <param name="serverIdentifierProvider">This server's own DUID.</param>
/// <param name="options">This server's own core configuration (Rapid Commit eligibility).</param>
/// <param name="logger">The logger this handler's own dispatch decisions are recorded to.</param>
public sealed class Dhcpv6RequestHandler(
    IServiceScopeFactory scopeFactory,
    IDhcpv6ServerIdentifierProvider serverIdentifierProvider,
    IOptions<DhcpServerOptions> options,
    ILogger<Dhcpv6RequestHandler> logger
) : IDhcpv6RequestHandler, IDhcpv6RelayMessageHandler
{
    /// <summary>
    ///     This type carries everything this handler learns from decoding an incoming RFC 4704
    ///     Client FQDN option (code 39), computed once up front so the same values can both feed
    ///     <see cref="Ninlil.Dhcp.Server.Core.Allocation.IDhcpv6LeaseAllocationService.ConfirmAddressAsync" />'s
    ///     own <c>hostName</c> parameter and build this handler's own reply option afterward,
    ///     without decoding the request twice.
    /// </summary>
    /// <param name="HadOption">
    ///     Whether the request carried a Client FQDN option at all -- when
    ///     <see langword="false" />, this handler never adds one to its own reply either (RFC 4704
    ///     is a purely opt-in exchange; a server has nothing to say back to a client that never
    ///     asked).
    /// </param>
    /// <param name="ClientRequestedServerAaaaUpdate">
    ///     The client's own "S" bit, as sent -- used only to compute the reply's own "O"
    ///     (override) bit per RFC 4704 Section 4.1's "A server MUST set this bit to 1 if the 'S'
    ///     bit in its reply to the client does not match the 'S' bit received from the client."
    /// </param>
    /// <param name="DomainName">
    ///     The client's own decoded domain name, echoed back verbatim in this handler's own reply
    ///     -- RFC 4704 Section 6 explicitly allows this: "The server MAY simply copy the Domain
    ///     Name field from the Client FQDN option that the client sent to the server." This
    ///     project deliberately takes that option rather than re-deriving the zone-qualified name
    ///     it will actually register, which happens later, decoupled, and asynchronously, inside
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c> -- duplicating that
    ///     zone-name-composition logic here, just to report it a few milliseconds earlier in the
    ///     same exchange, would risk the two disagreeing without giving a real client any benefit
    ///     it depends on.
    /// </param>
    /// <param name="IsFullyQualified">The client's own decoded "fully qualified" flag, echoed back alongside <see cref="DomainName" />.</param>
    /// <param name="HostNameCandidate">
    ///     The single DNS label this handler will offer to
    ///     <see cref="Ninlil.Dhcp.Server.Core.Allocation.IDhcpv6LeaseAllocationService.ConfirmAddressAsync" />
    ///     as the lease's own host name -- the first label of <see cref="DomainName" />, or
    ///     <see langword="null" /> when the client left the Domain Name field empty (RFC 4704
    ///     Section 4.2's "a client MAY ... leave the Domain Name field empty if it desires the
    ///     server to provide a name" -- this project never auto-generates one, the same honest
    ///     "no usable host name, no registration" behavior DHCPv4's own Host Name option handling
    ///     already establishes) or explicitly declined any server-performed update via the "N" bit
    ///     (RFC 4704 defines no server override for "N" the way it does for "S" -- see this
    ///     record's own construction site for the full reasoning).
    /// </param>
    private readonly record struct ClientFqdnRequest(
        bool HadOption,
        bool ClientRequestedServerAaaaUpdate,
        string DomainName,
        bool IsFullyQualified,
        string HostNameCandidate
    );

    /// <summary>
    ///     This constant bounds how many times a RELAY-FORW may already have been relayed (RFC
    ///     8415 Section 7.1's own "hop-count" field) before this method refuses to process it at
    ///     all -- the same defensive, RFC-independent loop-prevention reasoning
    ///     <c>Ninlil.Dhcp.Server.Relay.Dhcpv4RelayAgent.MaxHops</c>'s own doc comment already
    ///     documents, applied here against a hostile or malformed inbound hop count rather than
    ///     this project's own outbound forwarding (this project's own relay support only ever
    ///     forwards a single hop -- see <see cref="Dhcpv6RelayMessage" />'s own doc comment -- so
    ///     this class never itself produces an already-incremented value to check against).
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
        // A RELAY-REPL reaching a server engine would mean some other relay's or server's own
        // reply was misdirected here -- never a message this engine itself should act on.
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

        // RFC 8415 Section 18.2's link-selection guidance: a relayed request's own link-address
        // identifies which of this server's own scopes the client actually belongs to, since a
        // relayed request's own UDP source address is the relay agent's, not the client's --
        // Dhcpv6ScopeResolver's own new relay-address resolution step reads exactly this.
        innerRequest.RelayLinkAddress = message.LinkAddress;

        // Threaded onto the unwrapped inner message the same way RelayLinkAddress is, purely for
        // a tag-based conditional option delivery rule (Dhcpv6OptionOverlayService) to match
        // against later -- neither option is itself part of RFC 8415 Section 8's own client/server
        // wire format.
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
            // RFC 8415 Section 16 has a server copy hop-count/link-address/peer-address from the
            // RELAY-FORW it answers, unchanged, into its own RELAY-REPL.
            HopCount = message.HopCount,
            LinkAddress = message.LinkAddress,
            PeerAddress = message.PeerAddress,
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerReply));

        // RFC 8415 Section 21.18 requires echoing an Interface-ID option back unchanged, if the
        // relay agent included one.
        if (message.TryGetInterfaceId(out byte[] interfaceId))
            relayReply.SetInterfaceId(interfaceId);

        return relayReply;
    }

    /// <summary>
    ///     This method holds the actual per-message processing logic <see cref="HandleRequestAsync" />
    ///     and <see cref="HandleRelayMessageAsync" /> both call -- an unrelayed client message and
    ///     a RELAY-FORW's own unwrapped inner message are handled identically from this point on,
    ///     the only difference being whether <see cref="Dhcpv6Message.RelayLinkAddress" /> was
    ///     already set by the caller before this method ever sees it.
    /// </summary>
    private async Task<Dhcpv6Message> ProcessMessageAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        // RFC 8415 requires a Client Identifier on essentially every client message type this
        // engine handles; a message without one carries no way to look up, or even name, a lease
        // at all.
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

        return request.MessageType switch
        {
            Dhcpv6MessageTypeEnum.Solicit => await HandleSolicitAsync(
                request,
                clientKey,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Request => await HandleConfirmingExchangeAsync(
                request,
                clientKey,
                requireServerIdentifierMatch: true,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Renew => await HandleConfirmingExchangeAsync(
                request,
                clientKey,
                requireServerIdentifierMatch: true,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
                cancellationToken
            ),
            Dhcpv6MessageTypeEnum.Rebind => await HandleConfirmingExchangeAsync(
                request,
                clientKey,
                requireServerIdentifierMatch: false,
                scopeResolver,
                leaseAllocationService,
                optionOverlayService,
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

            // ADVERTISE/REPLY/RECONFIGURE are server-originated; a client (or something
            // malformed/malicious) sending one this server ever receives has nothing sensible to
            // answer.
            _ => null,
        };
    }

    /// <summary>
    ///     This method handles a SOLICIT, per RFC 8415 Section 18.3.9: resolve a scope for each
    ///     present IA_NA/IA_PD, tentatively allocate a value for it, and advertise it back -- or,
    ///     if the client asked for Rapid Commit and this server allows it
    ///     (<see cref="DhcpServerOptions.EnableDhcpv6RapidCommit" />), commit directly and reply
    ///     immediately instead of merely tentatively offering.
    /// </summary>
    private async Task<Dhcpv6Message> HandleSolicitAsync(
        Dhcpv6Message request,
        string clientKey,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        IDhcpv6OptionOverlayService optionOverlayService,
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
        DhcpScopeEntity optionScope = null;

        // Only a Rapid-Commit exchange actually commits an address in this same message -- see
        // ClientFqdnRequest's own doc comment for why an ordinary tentative ADVERTISE never
        // processes a Client FQDN option at all (there is nothing yet to honestly report back).
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
                optionScope ??= scope;

                // A reply association is added whenever a scope was resolved at all, even when
                // nothing could actually be allocated (it then carries a Status Code option
                // instead) -- only an association that actually carries an address counts as
                // "something was genuinely offered" for this method's own overall silence rule.
                if (replyNa.Addresses.Count > 0)
                    offeredAnything = true;

                if (rapidCommit)
                    ApplyClientFqdnReply(reply, clientFqdn, replyNa.Addresses.Count > 0);
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
                optionScope ??= scope;

                // See the matching IA_NA comment above for why this checks Prefixes.Count rather
                // than merely "was a reply association object produced at all."
                if (replyPd.Prefixes.Count > 0)
                    offeredAnything = true;
            }
        }

        // RFC 8415 Section 18.3.9: if this server has nothing to offer for any IA in the SOLICIT
        // at all, it should not send an ADVERTISE.
        if (!offeredAnything)
            return null;

        if (rapidCommit)
            reply.SetRapidCommit();

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
    ///     This method handles REQUEST/RENEW/REBIND, per RFC 8415 Sections 18.3.2/18.3.4/18.3.5 --
    ///     see this class's own doc comment for why these three message types share one handler.
    /// </summary>
    private async Task<Dhcpv6Message> HandleConfirmingExchangeAsync(
        Dhcpv6Message request,
        string clientKey,
        bool requireServerIdentifierMatch,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        IDhcpv6OptionOverlayService optionOverlayService,
        CancellationToken cancellationToken
    )
    {
        if (
            requireServerIdentifierMatch
            && (
                !request.TryGetServerIdentifier(out byte[] requestedServerDuid)
                || !requestedServerDuid.AsSpan().SequenceEqual(serverIdentifierProvider.Value)
            )
        )
            // Either no Server Identifier at all, or one naming a different server -- not
            // addressed to us; RFC 8415 calls for silence, not an error reply.
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
            ApplyClientFqdnReply(reply, clientFqdn, replyNa.Addresses.Count > 0);
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
    ///     This method handles a RELEASE, per RFC 8415 Section 18.3.6 -- unlike DHCPv4's own
    ///     RELEASE, a DHCPv6 server always replies.
    /// </summary>
    private async Task<Dhcpv6Message> HandleReleaseAsync(
        Dhcpv6Message request,
        string clientKey,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        CancellationToken cancellationToken
    )
    {
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
    ///     This method handles a DECLINE, per RFC 8415 Section 18.3.7: the client reports that one
    ///     or more addresses in its first IA_NA are already in use by something else on the
    ///     network (found via duplicate address detection). Unlike DHCPv4's own DECLINE, a DHCPv6
    ///     server always replies. Prefix Delegation has no DECLINE analogue at all.
    /// </summary>
    private async Task<Dhcpv6Message> HandleDeclineAsync(
        Dhcpv6Message request,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6LeaseAllocationService leaseAllocationService,
        CancellationToken cancellationToken
    )
    {
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestedNa = FirstOrDefaultNa(request);

        if (requestedNa is null)
            return null;

        DhcpScopeEntity scope = await scopeResolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            cancellationToken
        );

        if (scope is not null)
            foreach (Dhcpv6IdentityAssociationAddress declined in requestedNa.Addresses)
                await leaseAllocationService.DeclineAddressAsync(
                    scope,
                    declined.Address,
                    cancellationToken
                );

        Dhcpv6Message reply = NewReply(request, Dhcpv6MessageTypeEnum.Reply);
        reply.AddIdentityAssociationForNonTemporaryAddresses(
            BuildNaStatus(requestedNa.IdentityAssociationId, Dhcpv6StatusCodeEnum.Success)
        );
        Dhcpv6OptionDeliveryService.Apply(reply, scope, request, serverIdentifierProvider.Value);
        return reply;
    }

    /// <summary>
    ///     This method handles a CONFIRM, per RFC 8415 Section 18.3.3: no allocation happens at
    ///     all -- this purely checks whether every address across every IA_NA the client presents
    ///     is still appropriate for whichever link this server believes the client to be on
    ///     (approximated, absent real relay-agent link information, as "falls within some enabled
    ///     address-assignment scope's own configured range"). If this server has no
    ///     address-assignment scope configured at all, it has no configuration information to
    ///     judge with, and RFC 8415 calls for silence rather than a guess.
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
    ///     This method handles an INFORMATION-REQUEST, per RFC 8415 Section 18.3.5: the client
    ///     already has an address through some other means and is purely asking for additional
    ///     local configuration parameters (this engine's own DNS server addresses, today). No IA
    ///     processing at all -- RFC 8415 says a client MUST NOT include one.
    /// </summary>
    private async Task<Dhcpv6Message> HandleInformationRequestAsync(
        Dhcpv6Message request,
        IDhcpv6ScopeResolver scopeResolver,
        IDhcpv6OptionOverlayService optionOverlayService,
        CancellationToken cancellationToken
    )
    {
        // Reuses the ordinary address-assignment scope resolver purely for its own "single
        // enabled scope" fallback -- an Information-Request client has no lease/reservation of
        // its own to match against, so steps 1/2 simply find nothing and fall through to it.
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
    ///     This method resolves an address-assignment scope for one IA_NA and tentatively offers
    ///     it an address, for a SOLICIT without Rapid Commit.
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
    ///     This method resolves an address-assignment scope for one IA_NA and commits its own
    ///     hinted address to it -- REQUEST/RENEW/REBIND only; a client always names the exact
    ///     address it already holds (or was offered) in these three exchanges, unlike a
    ///     Rapid-Commit SOLICIT, which supplies no hint at all and is instead handled by
    ///     <see cref="RapidCommitAddressAssociationAsync" />.
    /// </summary>
    /// <param name="scopeResolver">The resolver used to find the address-assignment scope to commit against.</param>
    /// <param name="leaseAllocationService">The service the actual commit is delegated to.</param>
    /// <param name="request">The request being answered.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="requested">The specific IA_NA being committed.</param>
    /// <param name="hostName">
    ///     The host name to record for this lease, if the request carried a usable RFC 4704 Client
    ///     FQDN option -- see <see cref="ClientFqdnRequest.HostNameCandidate" />'s own doc comment.
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
    ///     This method resolves an address-assignment scope for one IA_NA, allocates a fresh
    ///     address for it exactly as an ordinary (non-Rapid-Commit) SOLICIT would (see
    ///     <see cref="OfferAddressAssociationAsync" />), and immediately commits that same address
    ///     -- the Rapid Commit analogue of a full SOLICIT-then-REQUEST exchange compressed into
    ///     one message. Unlike <see cref="CommitAddressAssociationAsync" />, this never reads a
    ///     hint out of <paramref name="requested" />: a client's first-ever SOLICIT for a given
    ///     IAID carries no address at all to hint with, since it does not yet know what this
    ///     server would assign.
    /// </summary>
    /// <param name="scopeResolver">The resolver used to find the address-assignment scope to allocate from.</param>
    /// <param name="leaseAllocationService">The service the actual offer-then-commit sequence is delegated to.</param>
    /// <param name="request">The request being answered.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="requested">The specific IA_NA being committed.</param>
    /// <param name="hostName">
    ///     The host name to record for this lease, if the request carried a usable RFC 4704 Client
    ///     FQDN option -- see <see cref="ClientFqdnRequest.HostNameCandidate" />'s own doc comment.
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
    ///     This method resolves a Prefix Delegation scope for one IA_PD and tentatively offers it
    ///     a prefix, for a SOLICIT without Rapid Commit -- the Prefix Delegation analogue of
    ///     <see cref="OfferAddressAssociationAsync" />.
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
    ///     This method resolves a Prefix Delegation scope for one IA_PD and commits its own
    ///     hinted prefix to it -- REQUEST/RENEW/REBIND only, the Prefix Delegation analogue of
    ///     <see cref="CommitAddressAssociationAsync" />; see that method's own doc comment for why
    ///     a Rapid-Commit SOLICIT needs its own separate handling instead
    ///     (<see cref="RapidCommitPrefixAssociationAsync" />).
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
    ///     This method resolves a Prefix Delegation scope for one IA_PD, allocates a fresh prefix
    ///     for it exactly as an ordinary (non-Rapid-Commit) SOLICIT would (see
    ///     <see cref="OfferPrefixAssociationAsync" />), and immediately commits that same prefix --
    ///     the Prefix Delegation analogue of <see cref="RapidCommitAddressAssociationAsync" />.
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
    ///     This method resolves a scope of a given kind and, if one is found, releases whatever
    ///     matching lease this client holds in it.
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
    ///     This method finds the first IA_NA a request carries, per this class's own doc comment
    ///     on only ever processing the first of each kind.
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
    ///     This method finds the first IA_PD a request carries, per this class's own doc comment
    ///     on only ever processing the first of each kind.
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
    ///     This method builds an IA_NA carrying nothing but an echoed IAID and a Status Code
    ///     option -- used for a RELEASE/DECLINE reply, where no address is ever handed back.
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
    ///     This method builds an IA_PD carrying nothing but an echoed IAID and a Status Code
    ///     option -- the Prefix Delegation analogue of <see cref="BuildNaStatus" />.
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
    ///     This method builds the common shape every reply this handler ever sends shares: the
    ///     transaction identifier and the reply's own message type.
    /// </summary>
    private static Dhcpv6Message NewReply(
        Dhcpv6Message request,
        Dhcpv6MessageTypeEnum messageType
    ) => new() { MessageType = messageType, TransactionId = request.TransactionId };

    /// <summary>
    ///     This method decodes an incoming RFC 4704 Client FQDN option, if the request carries
    ///     one, into a <see cref="ClientFqdnRequest" /> -- called once per exchange, before the
    ///     matching address commit, so <see cref="ClientFqdnRequest.HostNameCandidate" /> is ready
    ///     to hand to
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

        // RFC 4704 Section 4.1 defines no server override for the "N" bit the way it does for
        // "S" (see ClientFqdnRequest's own doc comment) -- a client explicitly asking for no DNS
        // updates at all is honored outright, by simply never offering a host name to register,
        // rather than this server unilaterally deciding it knows better. A client that left the
        // Domain Name field empty altogether offers no name to extract in the first place.
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
    ///     This method attaches this handler's own Client FQDN reply option to
    ///     <paramref name="reply" />, once the outcome of the matching address commit is known --
    ///     a no-op when the request never carried one (see <see cref="ClientFqdnRequest.HadOption" />'s
    ///     own doc comment). This server's own fixed policy is to always take responsibility for a
    ///     lease's AAAA (and, as a free side effect of the same write, PTR) record whenever it
    ///     actually has a host name to register -- <paramref name="addressWasGranted" /> together
    ///     with <see cref="ClientFqdnRequest.HostNameCandidate" /> is exactly the condition
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c> itself checks before
    ///     ever writing a record, so this reply's own "S"/"N" bits report that same real outcome
    ///     rather than a separately-tracked guess.
    /// </summary>
    private static void ApplyClientFqdnReply(
        Dhcpv6Message reply,
        ClientFqdnRequest decoded,
        bool addressWasGranted
    )
    {
        if (!decoded.HadOption)
            return;

        bool serverPerformsAaaaUpdate = addressWasGranted && decoded.HostNameCandidate is not null;

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

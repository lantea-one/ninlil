using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This class implements <see cref="IDhcpv4LeaseAllocationService" /> against the real
///     <see cref="ILeaseRepository" />/<see cref="IDhcpReservationRepository" /> repositories.
///     <para>
///         <b>Design note -- one lease row per (scope, client), reused across its whole
///         lifecycle</b>: <c>LeaseConfiguration</c> enforces a unique index on both
///         <c>(ScopeName, ClientIdentifier)</c> and <c>(ScopeName, IpAddress)</c>, so a client can
///         hold at most one lease per scope, and an address can be held by at most one lease per
///         scope, at any moment. This class leans on that directly: rather than writing a new
///         historical row every time a client's own state changes (offered -&gt; active -&gt;
///         released, for example), it finds and updates that one row in place, only ever removing
///         it (via <c>OfferAsync</c>'s own sticky-reuse path, or explicitly when a different
///         client's stale row already occupies a target address) when a real address change
///         genuinely requires it.
///     </para>
///     <para>
///         <b>Design note -- representing a DECLINE without a new enum member</b>:
///         <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum" /> is shared across both DHCPv4 and a
///         future DHCPv6 engine and has no dedicated "declined" member. Every pool-availability
///         check in this class (<see cref="IsLeaseBlocking" />) already keys off two things
///         together -- the lease's own <c>State</c> not being <c>Released</c>, and its
///         <c>LeaseExpiresAtUtc</c> still being in the future -- so <see cref="DeclineAsync" />
///         faithfully represents a quarantine by setting <c>State</c> to <c>Expired</c> with
///         <c>LeaseExpiresAtUtc</c> pushed <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" />
///         into the future: every allocation path that would otherwise treat an <c>Expired</c>
///         lease as free correctly keeps treating it as unavailable for as long as that quarantine
///         window has not yet elapsed, for every client, not only the one that declined it --
///         without adding a new member to a shared, cross-protocol enum for this one call site's
///         benefit.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository this service reads and writes leases through.</param>
/// <param name="reservationRepository">The repository this service reads static reservations through.</param>
/// <param name="unitOfWork">The unit of work this service commits every change through.</param>
/// <param name="serverIdentifierProvider">
///     This server's own identifier address, excluded from pool allocation so it is never handed
///     out to a client.
/// </param>
/// <param name="options">This server's own core configuration.</param>
/// <param name="eventPublisher">
///     The publisher a real grant (<see cref="ConfirmAsync" />) or release
///     (<see cref="ReleaseAsync" />) reports through, via
///     <see cref="DhcpLeaseChangedEvent" /> -- most notably consumed by the DHCP-&gt;DNS
///     integration feature, <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseDnsRegistrationEventSubscriber</c>.
///     Never published for <see cref="OfferAsync" />/<see cref="DeclineAsync" />: an OFFER is
///     only ever tentative, and a DECLINE quarantines an address rather than granting one.
/// </param>
/// <param name="logger">The logger this service's own allocation decisions are recorded to.</param>
public sealed class Dhcpv4LeaseAllocationService(
    ILeaseRepository leaseRepository,
    IDhcpReservationRepository reservationRepository,
    IUnitOfWork unitOfWork,
    IDhcpServerIdentifierProvider serverIdentifierProvider,
    IOptions<DhcpServerOptions> options,
    IDnsServerEventPublisher eventPublisher,
    ILogger<Dhcpv4LeaseAllocationService> logger
) : IDhcpv4LeaseAllocationService
{
    /// <inheritdoc />
    public async Task<LeaseEntity> OfferAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    )
    {
        string clientKey = Dhcpv4ClientIdentifier.Format(request);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListByScopeNameAsync(
            scope.Name,
            cancellationToken
        );
        LeaseEntity existing = leases.FirstOrDefault(lease =>
            Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
        );

        IPAddress candidate = null;

        // Sticky reuse: a client that already has an Offered/Active/Released row in this scope
        // keeps the same address, as long as it still falls within the scope's own current range
        // -- friendlier to the client, and free of the unique-index concerns a genuine address
        // *change* would raise, since this is the exact same row. Deliberately excludes a row in
        // the Expired state: that is this class's own quarantine sentinel for a DECLINEd address
        // (see this class's own doc comment), and reusing it here would silently hand the very
        // same client its own just-declined, still-quarantined address straight back on its next
        // DISCOVER -- exactly the outcome the quarantine exists to prevent for every client,
        // including the one that declined it.
        if (
            existing is { State: not LeaseStateEnum.Expired }
            && IPAddress.TryParse(existing.IpAddress, out IPAddress existingAddress)
            && IsWithinScopeRange(scope, existingAddress)
        )
            candidate = existingAddress;
        else
            candidate = await ResolveCandidateAddressAsync(
                scope,
                request,
                clientKey,
                leases,
                cancellationToken
            );

        if (candidate is null)
        {
            logger.LogWarning(
                "No address is currently available in scope '{ScopeName}' to offer client {ClientIdentifier}.",
                scope.Name,
                clientKey
            );
            return null;
        }

        bool isNewLease = existing is null;

        if (isNewLease)
        {
            // A brand-new row for this client is about to claim candidate -- if a stale,
            // non-blocking row for a *different* client (typically one that RELEASEd, or whose
            // lease has since naturally expired) already occupies that exact address, it must be
            // removed first, in its own SaveChanges, so claiming the address never races the same
            // (ScopeName, IpAddress) unique index within one transaction. A blocking row can never
            // reach this point at all -- every path that produced candidate (sticky reuse,
            // reservation, requested-address, and the pool scan) already excludes any address a
            // blocking lease currently occupies.
            LeaseEntity occupying = leases.FirstOrDefault(lease =>
                string.Equals(
                    lease.IpAddress,
                    candidate.ToString(),
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (occupying is not null)
            {
                leaseRepository.Remove(occupying);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        LeaseEntity lease = existing ?? NewLease(scope, clientKey);

        lease.IpAddress = candidate.ToString();
        lease.State = LeaseStateEnum.Offered;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.OfferHoldDuration);

        if (request.TryGetHostName(out string hostName))
            lease.HostName = hostName;

        await SaveAsync(lease, isNewLease, now, cancellationToken);
        return lease;
    }

    /// <inheritdoc />
    public async Task<LeaseEntity> ConfirmAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress address,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        string clientKey = Dhcpv4ClientIdentifier.Format(request);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DhcpReservationEntity reservation = await reservationRepository.FindByClientIdentifierAsync(
            scope.Id,
            clientKey,
            cancellationToken
        );
        bool addressMatchesOwnReservation =
            reservation is { IsDisabled: false }
            && string.Equals(
                reservation.IpAddress,
                address.ToString(),
                StringComparison.OrdinalIgnoreCase
            );

        // A client holding a reservation must use its reserved address, not any other -- it may
        // still confirm that exact address here, but never a different one.
        if (reservation is { IsDisabled: false } && !addressMatchesOwnReservation)
            return null;

        if (!addressMatchesOwnReservation)
        {
            // Ordinary pool rules apply only when the address being confirmed is not this
            // client's own reserved one: a reservation is a deliberate, operator-configured
            // address that need not fall within the scope's own dynamic pool range at all (a
            // fixed address for one specific device is a common, legitimate reason to reserve one
            // outside the range everything else draws from), so the range check below is
            // deliberately skipped in that case.
            if (!IsWithinScopeRange(scope, address))
                return null;

            DhcpReservationEntity addressReservation =
                await reservationRepository.FindByIpAddressAsync(
                    scope.Id,
                    address.ToString(),
                    cancellationToken
                );

            // Any reservation found this way must belong to a different client -- if it were this
            // client's own, the check above would already have matched it, since a scope can
            // never hold two reservations for the same client or the same address.
            if (addressReservation is { IsDisabled: false })
                return null;
        }

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListByScopeNameAsync(
            scope.Name,
            cancellationToken
        );
        LeaseEntity existingByClient = leases.FirstOrDefault(lease =>
            Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
        );
        LeaseEntity existingByAddress = leases.FirstOrDefault(lease =>
            string.Equals(lease.IpAddress, address.ToString(), StringComparison.OrdinalIgnoreCase)
        );

        if (existingByAddress is not null && !ReferenceEquals(existingByAddress, existingByClient))
        {
            if (IsLeaseBlocking(existingByAddress, now))
                // A different client currently holds (or has quarantined) this exact address.
                return null;

            // A stale, non-blocking row for a different client already occupies this address --
            // remove it first, in its own SaveChanges, so freeing this address and claiming it
            // never race the same (ScopeName, IpAddress) unique index within one transaction.
            leaseRepository.Remove(existingByAddress);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        bool isNewLease = existingByClient is null;
        LeaseEntity lease = existingByClient ?? NewLease(scope, clientKey);

        lease.IpAddress = address.ToString();
        lease.State = LeaseStateEnum.Active;
        lease.LeaseObtainedAtUtc ??= now;
        lease.LeaseExpiresAtUtc = now.Add(leaseDuration);

        if (request.TryGetHostName(out string hostName))
            lease.HostName = hostName;

        await SaveAsync(lease, isNewLease, now, cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Granted)
        );

        return lease;
    }

    /// <inheritdoc />
    public async Task DeclineAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress declinedAddress,
        CancellationToken cancellationToken = default
    )
    {
        string clientKey = Dhcpv4ClientIdentifier.Format(request);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Looked up by address, not by client: the point of a decline is to quarantine the
        // address itself, regardless of which row (if any) currently references it.
        LeaseEntity lease = await leaseRepository.FindByIpAddressAsync(
            scope.Name,
            declinedAddress.ToString(),
            cancellationToken
        );
        bool isNewLease = lease is null;
        lease ??= NewLease(scope, clientKey, declinedAddress);

        lease.State = LeaseStateEnum.Expired;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.DeclinedAddressQuarantineDuration);

        await SaveAsync(lease, isNewLease, now, cancellationToken);

        logger.LogWarning(
            "Client {ClientIdentifier} declined {Address} in scope '{ScopeName}'; quarantining it until {QuarantineUntilUtc:o}.",
            clientKey,
            declinedAddress,
            scope.Name,
            lease.LeaseExpiresAtUtc
        );
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress releasedAddress,
        CancellationToken cancellationToken = default
    )
    {
        string clientKey = Dhcpv4ClientIdentifier.Format(request);
        LeaseEntity lease = await leaseRepository.FindByClientIdentifierAsync(
            scope.Name,
            clientKey,
            cancellationToken
        );

        if (
            lease is null
            || !string.Equals(
                lease.IpAddress,
                releasedAddress.ToString(),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            // A client releasing an address this server has no matching record of is not an
            // error worth acting on -- it simply has nothing to free.
            logger.LogInformation(
                "Ignoring a RELEASE of {Address} from {ClientIdentifier} in scope '{ScopeName}': no matching lease is on record.",
                releasedAddress,
                clientKey,
                scope.Name
            );
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        lease.State = LeaseStateEnum.Released;
        lease.LeaseExpiresAtUtc = now;
        lease.UpdatedAtUtc = now;

        leaseRepository.Update(lease);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Released)
        );
    }

    /// <summary>
    ///     This method resolves the address to offer a client with no already-held, still-in-range
    ///     address of its own: a static reservation, if one exists for this client; otherwise the
    ///     client's own requested address, if free and in range; otherwise the first free address
    ///     found scanning the scope's pool in order.
    /// </summary>
    private async Task<IPAddress> ResolveCandidateAddressAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        string clientKey,
        IReadOnlyList<LeaseEntity> leases,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DhcpReservationEntity reservation = await reservationRepository.FindByClientIdentifierAsync(
            scope.Id,
            clientKey,
            cancellationToken
        );

        if (
            reservation is { IsDisabled: false }
            && IPAddress.TryParse(reservation.IpAddress, out IPAddress reservedAddress)
        )
        {
            LeaseEntity conflicting = leases.FirstOrDefault(lease =>
                string.Equals(
                    lease.IpAddress,
                    reservation.IpAddress,
                    StringComparison.OrdinalIgnoreCase
                ) && !Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
            );

            if (conflicting is null || !IsLeaseBlocking(conflicting, now))
                return reservedAddress;

            logger.LogWarning(
                "The reservation for {ClientIdentifier} in scope '{ScopeName}' ({Address}) conflicts with an existing lease held by a different client.",
                clientKey,
                scope.Name,
                reservedAddress
            );
            return null;
        }

        IPAddress serverIdentifier = serverIdentifierProvider.Value;

        // The client's own requested address (RFC 2132 Section 9.1), honored if it is free, in
        // range, and not reserved to someone else.
        if (
            request.TryGetRequestedIpAddress(out IPAddress requestedAddress)
            && IsWithinScopeRange(scope, requestedAddress)
            && !requestedAddress.Equals(serverIdentifier)
        )
        {
            DhcpReservationEntity requestedAddressReservation =
                await reservationRepository.FindByIpAddressAsync(
                    scope.Id,
                    requestedAddress.ToString(),
                    cancellationToken
                );

            if (requestedAddressReservation is not { IsDisabled: false })
            {
                LeaseEntity conflicting = leases.FirstOrDefault(lease =>
                    string.Equals(
                        lease.IpAddress,
                        requestedAddress.ToString(),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (conflicting is null || !IsLeaseBlocking(conflicting, now))
                    return requestedAddress;
            }
        }

        // Fall back to scanning the pool itself, in ascending order, for the first address that
        // is neither reserved to a different client nor currently occupied by a blocking lease.
        if (
            !IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
            || !IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        )
        {
            logger.LogWarning(
                "Scope '{ScopeName}' has an invalid address range ({RangeStart}-{RangeEnd}) and cannot allocate any address.",
                scope.Name,
                scope.RangeStartAddress,
                scope.RangeEndAddress
            );
            return null;
        }

        IReadOnlyList<DhcpReservationEntity> allReservations =
            await reservationRepository.ListAsync(
                candidateReservation =>
                    candidateReservation.ScopeId == scope.Id && !candidateReservation.IsDisabled,
                cancellationToken
            );
        HashSet<string> reservedAddresses = allReservations
            .Select(candidateReservation => candidateReservation.IpAddress)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, LeaseEntity> leasesByAddress = leases.ToDictionary(
            lease => lease.IpAddress,
            lease => lease,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (IPAddress candidate in Dhcpv4NetworkHelper.Enumerate(rangeStart, rangeEnd))
        {
            string candidateKey = candidate.ToString();

            if (candidate.Equals(serverIdentifier))
                continue;

            if (reservedAddresses.Contains(candidateKey))
                continue;

            if (
                leasesByAddress.TryGetValue(candidateKey, out LeaseEntity occupyingLease)
                && IsLeaseBlocking(occupyingLease, now)
            )
                continue;

            return candidate;
        }

        return null;
    }

    /// <summary>
    ///     This method reports whether an address is currently unavailable per an existing lease
    ///     row for it: a released lease is always immediately available again, and any other
    ///     lease (offered, active, or quarantined via <see cref="DeclineAsync" />'s own use of
    ///     <c>Expired</c>) blocks the address for as long as its own <c>LeaseExpiresAtUtc</c>
    ///     has not yet passed.
    /// </summary>
    private static bool IsLeaseBlocking(LeaseEntity lease, DateTimeOffset now) =>
        lease.State != LeaseStateEnum.Released
        && (!lease.LeaseExpiresAtUtc.HasValue || lease.LeaseExpiresAtUtc.Value > now);

    /// <summary>
    ///     This method reports whether an address falls within a scope's own configured range.
    /// </summary>
    private static bool IsWithinScopeRange(DhcpScopeEntity scope, IPAddress address) =>
        IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
        && IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        && Dhcpv4NetworkHelper.IsWithinRange(address, rangeStart, rangeEnd);

    /// <summary>
    ///     This method constructs a brand-new lease entity with its own identifier and creation
    ///     timestamp already assigned, per this project's own "the caller that creates an entity
    ///     assigns its identifier" convention (see <see cref="Ninlil.Dns.Data.Abstractions.Entity.EntityBase" />'s
    ///     own doc comment).
    /// </summary>
    private static LeaseEntity NewLease(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress ipAddress = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = scope.Name,
            ClientIdentifier = clientKey,
            IpAddress = ipAddress?.ToString(),
        };

    /// <summary>
    ///     This method stages and commits a lease's own insert or update, stamping
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.AuditableEntityBase.UpdatedAtUtc" /> for
    ///     an existing row.
    /// </summary>
    private async Task SaveAsync(
        LeaseEntity lease,
        bool isNewLease,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (isNewLease)
        {
            await leaseRepository.AddAsync(lease, cancellationToken);
        }
        else
        {
            lease.UpdatedAtUtc = now;
            leaseRepository.Update(lease);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

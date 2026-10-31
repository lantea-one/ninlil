using System.Net;
using System.Net.Sockets;
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
///     This class implements <see cref="IDhcpv4LeaseAllocationService" /> on the lease and
///     reservation repositories. It is scoped; <see cref="Dhcpv4RequestHandler" /> resolves it in
///     a new scope per request.
///     <para>
///         <b>One lease row per client and scope.</b> <c>LeaseConfiguration</c> has unique indexes
///         on <c>(ScopeName, ClientIdentifier)</c> and <c>(ScopeName, IpAddress)</c>, so a client
///         has at most one row per scope and an address at most one row. The row is updated in
///         place as its state changes (offered, active, released) rather than adding history
///         rows. Another client's stale row (released or expired) is deleted, in its own
///         <c>SaveChanges</c>, before an address it occupies is claimed, on every path
///         (<see cref="OfferAsync" /> and <see cref="ConfirmAsync" />), so the unique index never
///         rejects a claim.
///     </para>
///     <para>
///         <b>DECLINE without a "declined" state.</b> <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum" />,
///         shared with DHCPv6, has no declined member. Availability checks
///         (<see cref="IsLeaseBlocking" />) treat a row as blocking while it is not <c>Released</c>
///         and its <c>LeaseExpiresAtUtc</c> is in the future, so <see cref="DeclineAsync" />
///         re-keys the declining client's row to <c>declined:&lt;address&gt;</c>
///         (<see cref="DhcpDeclinedAddress" />), marks it <c>Expired</c> with an expiry
///         <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" /> ahead and clears its
///         host name. No client owns that row, so the address stays blocked for every client,
///         the declining client included, and the client's next DISCOVER starts a new row. A
///         DECLINE of an address that is free or leased to another client changes nothing. An
///         <c>Expired</c> row with a future expiry on the client's own key is treated as a
///         quarantine and re-keyed the same way when the client next sends a DISCOVER or
///         REQUEST (see <c>DetachQuarantineAsync</c>).
///     </para>
///     <para>
///         <b>Offer order</b> (RFC 2131 Section 4.3.1, after the reservation): the client's enabled
///         reservation (the manual allocation of RFC 2131 Section 1) first, then the address on the client's own row
///         when it is still usable, then the requested address, then the first free pool address.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository leases are read and written through.</param>
/// <param name="reservationRepository">The repository reservations are read through.</param>
/// <param name="scopeRepository">
///     The repository the scope's current name is read through after each lease write, so a row
///     follows a scope rename that committed meanwhile (see <c>FollowScopeRenameAsync</c>).
/// </param>
/// <param name="unitOfWork">The unit of work every change is saved through.</param>
/// <param name="serverIdentifierProvider">
///     The server identifier address, which the pool scan and the requested-address path never
///     hand out.
/// </param>
/// <param name="options">The server engine configuration (offer hold and quarantine times).</param>
/// <param name="eventPublisher">
///     The publisher a grant (<see cref="ConfirmAsync" />), release (<see cref="ReleaseAsync" />)
///     or decline (<see cref="DeclineAsync" />) is reported to as a <see cref="DhcpLeaseChangedEvent" />, used for example by
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseDnsRegistrationEventSubscriber</c>. A
///     decline publishes an <c>Expired</c> event, so the address loses its DNS record; offers
///     publish nothing, since an offer is tentative.
/// </param>
/// <param name="logger">The logger allocation problems are recorded to.</param>
public sealed class Dhcpv4LeaseAllocationService(
    ILeaseRepository leaseRepository,
    IDhcpReservationRepository reservationRepository,
    IDhcpScopeRepository scopeRepository,
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

        // A quarantine left on the client's own key is moved off the client, so the request
        // continues as for a client without a row.
        if (
            existing is not null
            && await DetachQuarantineAsync(scope, existing, now, cancellationToken)
        )
            existing = null;

        // RFC 2131 Section 4.3.1 order, preceded by the reservation (the manual allocation of
        // Section 1): the client's reservation, the address it already has, the address it asked for, then the first free pool address.
        (bool hasReservation, IPAddress candidate) = await ResolveReservationAsync(
            scope,
            clientKey,
            leases,
            now,
            cancellationToken
        );

        if (!hasReservation && existing is not null)
            candidate = await ResolveStickyAddressAsync(scope, existing, cancellationToken);

        if (!hasReservation && candidate is null)
            candidate = await ResolveCandidateAddressAsync(
                scope,
                request,
                existing,
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

        // Another client's row at the candidate is non-blocking (every path skipped blocking
        // rows), so delete it first, in its own SaveChanges. The client's row, new or not, would
        // otherwise hit the (ScopeName, IpAddress) unique index.
        await RemoveStaleRowAtAsync(leases, candidate, existing, cancellationToken);

        LeaseEntity lease = existing ?? NewLease(scope, clientKey);

        lease.IpAddress = candidate.ToString();
        lease.State = LeaseStateEnum.Offered;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.OfferHoldDuration);

        if (request.TryGetHostName(out string hostName))
            lease.HostName = hostName;

        await SaveAsync(scope, lease, isNewLease, now, cancellationToken);
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
        reservation = KeepOnlyUsable(scope, reservation);
        bool addressMatchesOwnReservation =
            reservation is { IsDisabled: false }
            && string.Equals(
                reservation.IpAddress,
                address.ToString(),
                StringComparison.OrdinalIgnoreCase
            );

        // A client with a reservation may confirm only its reserved address.
        if (reservation is { IsDisabled: false } && !addressMatchesOwnReservation)
            return null;

        if (!addressMatchesOwnReservation)
        {
            // Pool rules apply only to addresses that are not the client's reservation. A
            // reserved address may lie outside the dynamic range (a fixed address for one device
            // often does), so the range check is skipped for it.
            if (!IsWithinScopeRange(scope, address))
                return null;

            DhcpReservationEntity addressReservation =
                await reservationRepository.FindByIpAddressAsync(
                    scope.Id,
                    address.ToString(),
                    cancellationToken
                );

            // A reservation found here belongs to another client; the client's own would have
            // matched above.
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

        // A quarantine left on the client's own key no longer belongs to the client, so the
        // address stays blocked for it and the NAK below follows.
        if (
            existingByClient is not null
            && await DetachQuarantineAsync(scope, existingByClient, now, cancellationToken)
        )
            existingByClient = null;

        LeaseEntity existingByAddress = leases.FirstOrDefault(lease =>
            string.Equals(lease.IpAddress, address.ToString(), StringComparison.OrdinalIgnoreCase)
        );

        if (existingByAddress is not null && !ReferenceEquals(existingByAddress, existingByClient))
        {
            if (IsLeaseBlocking(existingByAddress, now))
                // Another client holds this address, or it is quarantined.
                return null;

            // Another client's non-blocking row holds this address; delete it first, in its own
            // SaveChanges, so claiming the address does not hit the unique index.
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

        await SaveAsync(scope, lease, isNewLease, now, cancellationToken);

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

        LeaseEntity lease = await leaseRepository.FindByIpAddressAsync(
            scope.Name,
            declinedAddress.ToString(),
            cancellationToken
        );

        // Only the client's own row may be quarantined. A missing row is not created, or one
        // host could decline every free address and empty the pool.
        if (
            lease is null
            || !string.Equals(lease.ClientIdentifier, clientKey, StringComparison.Ordinal)
        )
        {
            logger.LogWarning(
                "Ignored DECLINE of {Address} from {ClientIdentifier} in scope '{ScopeName}': not its lease.",
                declinedAddress,
                clientKey,
                scope.Name
            );
            return;
        }

        // The row is re-keyed so no client owns it: the address stays blocked for everyone and
        // the declining client's slot is free for its next DISCOVER.
        QuarantineRow(lease, declinedAddress, now);

        await SaveAsync(scope, lease, isNewLease: false, now, cancellationToken);

        // The row may have been Active (RFC 2131 Section 4.4.1: a client checks the address after
        // the ACK), so its DNS record and client identity go with it.
        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Expired)
        );

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
            // Nothing on record to free, which is not an error.
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
        await FollowScopeRenameAsync(scope, lease, cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Released)
        );
    }

    /// <summary>
    ///     This method returns <paramref name="reservation" /> when its address is an
    ///     IPv4 address, and <see langword="null" /> (with a Warning) when it is not, so the
    ///     client is treated as having no reservation. The reservations API refuses such an
    ///     address, but a restore or a direct database edit can still store one, and an address of
    ///     the wrong family makes the reply impossible to encode, so the device would get no
    ///     answer at all.
    /// </summary>
    private DhcpReservationEntity KeepOnlyUsable(
        DhcpScopeEntity scope,
        DhcpReservationEntity reservation
    )
    {
        if (
            reservation is null
            || !IPAddress.TryParse(reservation.IpAddress, out IPAddress reserved)
            || reserved.AddressFamily == AddressFamily.InterNetwork
        )
            return reservation;

        logger.LogWarning(
            "The reservation for {ClientIdentifier} in scope '{ScopeName}' ({Address}) is not an IPv4 address, so it is skipped and the client is treated as having no reservation. Correct or delete the reservation.",
            reservation.ClientIdentifier,
            scope.Name,
            reservation.IpAddress
        );
        return null;
    }

    /// <summary>
    ///     This method looks up the client's enabled reservation. A reservation is a manual
    ///     allocation (RFC 2131 Section 1) and outranks the client's current binding, and <see cref="ConfirmAsync" /> refuses
    ///     every other address, so a conflict gives no offer at all.
    /// </summary>
    /// <returns>
    ///     Whether the client has an enabled reservation, and the reserved address, or
    ///     <see langword="null" /> when another client holds that address or the reservation does
    ///     not parse.
    /// </returns>
    private async Task<(bool HasReservation, IPAddress Address)> ResolveReservationAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IReadOnlyList<LeaseEntity> leases,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        DhcpReservationEntity reservation = await reservationRepository.FindByClientIdentifierAsync(
            scope.Id,
            clientKey,
            cancellationToken
        );

        reservation = KeepOnlyUsable(scope, reservation);

        if (reservation is not { IsDisabled: false })
            return (false, null);

        if (!IPAddress.TryParse(reservation.IpAddress, out IPAddress reservedAddress))
            return (false, null);

        LeaseEntity conflicting = leases.FirstOrDefault(lease =>
            string.Equals(
                lease.IpAddress,
                reservation.IpAddress,
                StringComparison.OrdinalIgnoreCase
            ) && !Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
        );

        if (conflicting is null || !IsLeaseBlocking(conflicting, now))
            return (true, reservedAddress);

        logger.LogWarning(
            "The reservation for {ClientIdentifier} in scope '{ScopeName}' ({Address}) conflicts with an existing lease held by a different client.",
            clientKey,
            scope.Name,
            reservedAddress
        );
        return (true, null);
    }

    /// <summary>
    ///     This method returns the address on the client's own row when the client may keep it
    ///     (RFC 2131 Section 4.3.1, the client's previous address): in range, not the server
    ///     identifier and not reserved for another client. The row's state does not matter; a row
    ///     that expired naturally still names the client's previous address. Nobody else can hold
    ///     the address, because the unique index allows one row per address.
    /// </summary>
    private async Task<IPAddress> ResolveStickyAddressAsync(
        DhcpScopeEntity scope,
        LeaseEntity existing,
        CancellationToken cancellationToken
    )
    {
        if (
            !IPAddress.TryParse(existing.IpAddress, out IPAddress existingAddress)
            || !IsWithinScopeRange(scope, existingAddress)
            || existingAddress.Equals(serverIdentifierProvider.Value)
        )
            return null;

        // The client's own enabled reservation was handled first, so one found here is another
        // client's and ConfirmAsync would refuse the address.
        DhcpReservationEntity addressReservation = await reservationRepository.FindByIpAddressAsync(
            scope.Id,
            existingAddress.ToString(),
            cancellationToken
        );

        return addressReservation is { IsDisabled: false } ? null : existingAddress;
    }

    /// <summary>
    ///     This method chooses an address for a client with no usable reservation and no address
    ///     it can keep: the requested address when it is in range, not the server identifier, not
    ///     reserved and not blocked; otherwise the first address in the pool that is not the
    ///     server identifier, not reserved and not blocked. The client's own row never blocks.
    /// </summary>
    private async Task<IPAddress> ResolveCandidateAddressAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        LeaseEntity own,
        IReadOnlyList<LeaseEntity> leases,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IPAddress serverIdentifier = serverIdentifierProvider.Value;

        // The requested address (RFC 2132 Section 9.1), if free, in range and not reserved.
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
                    !ReferenceEquals(lease, own)
                    && string.Equals(
                        lease.IpAddress,
                        requestedAddress.ToString(),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (conflicting is null || !IsLeaseBlocking(conflicting, now))
                    return requestedAddress;
            }
        }

        // Scan the pool in ascending order for the first address that is not reserved and not
        // held by a blocking lease.
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

        Dictionary<string, LeaseEntity> leasesByAddress = leases
            .Where(lease => !ReferenceEquals(lease, own))
            .ToDictionary(
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
    ///     This method turns a lease row into the quarantine row of a declined address: re-keyed
    ///     to <see cref="DhcpDeclinedAddress.KeyFor" />, <c>Expired</c>, with an expiry
    ///     <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" /> ahead and no host
    ///     name. The caller saves.
    /// </summary>
    private void QuarantineRow(LeaseEntity lease, IPAddress address, DateTimeOffset now)
    {
        lease.ClientIdentifier = DhcpDeclinedAddress.KeyFor(address);
        lease.HostName = null;
        lease.State = LeaseStateEnum.Expired;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.DeclinedAddressQuarantineDuration);
    }

    /// <summary>
    ///     This method re-keys a client's own row when it is a quarantine (<c>Expired</c> with an
    ///     expiry still ahead) that still carries the client's key, keeping its expiry, and
    ///     reports it as an <c>Expired</c> lease change. A naturally expired row has a past expiry and stays
    ///     the client's row.
    /// </summary>
    /// <returns><see langword="true" /> when the row was re-keyed and saved.</returns>
    private async Task<bool> DetachQuarantineAsync(
        DhcpScopeEntity scope,
        LeaseEntity lease,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (
            lease.State != LeaseStateEnum.Expired
            || !lease.LeaseExpiresAtUtc.HasValue
            || lease.LeaseExpiresAtUtc.Value <= now
            || !IPAddress.TryParse(lease.IpAddress, out IPAddress address)
        )
            return false;

        lease.ClientIdentifier = DhcpDeclinedAddress.KeyFor(address);
        lease.HostName = null;

        await SaveAsync(scope, lease, isNewLease: false, now, cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Expired)
        );
        return true;
    }

    /// <summary>
    ///     This method deletes, in its own <c>SaveChanges</c>, the row of another client that
    ///     still holds an address about to be claimed. Callers have already skipped blocking rows,
    ///     so the row is released or expired.
    /// </summary>
    private async Task RemoveStaleRowAtAsync(
        IReadOnlyList<LeaseEntity> leases,
        IPAddress address,
        LeaseEntity own,
        CancellationToken cancellationToken
    )
    {
        LeaseEntity occupying = leases.FirstOrDefault(lease =>
            !ReferenceEquals(lease, own)
            && string.Equals(
                lease.IpAddress,
                address.ToString(),
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (occupying is null)
            return;

        leaseRepository.Remove(occupying);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     This method reports whether a lease row makes its address unavailable. A released row
    ///     never does; any other (offered, active, or <c>Expired</c> as a
    ///     <see cref="DeclineAsync" /> quarantine row) does until its <c>LeaseExpiresAtUtc</c> passes,
    ///     or forever if it has none.
    /// </summary>
    private static bool IsLeaseBlocking(LeaseEntity lease, DateTimeOffset now) =>
        lease.State != LeaseStateEnum.Released
        && (!lease.LeaseExpiresAtUtc.HasValue || lease.LeaseExpiresAtUtc.Value > now);

    /// <summary>
    ///     This method reports whether an address is within the scope's range.
    /// </summary>
    private static bool IsWithinScopeRange(DhcpScopeEntity scope, IPAddress address) =>
        IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
        && IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        && Dhcpv4NetworkHelper.IsWithinRange(address, rangeStart, rangeEnd);

    /// <summary>
    ///     This method creates a new IPv4 lease entity with its identifier and creation time set,
    ///     since the code that creates an entity assigns its identifier (see
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.EntityBase" />).
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
    ///     This method adds or updates a lease and saves, setting
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.AuditableEntityBase.UpdatedAtUtc" /> on an
    ///     existing row.
    /// </summary>
    private async Task SaveAsync(
        DhcpScopeEntity scope,
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
        await FollowScopeRenameAsync(scope, lease, cancellationToken);
    }

    /// <summary>
    ///     This method closes the race with a scope rename. <c>DhcpScopesController</c> moves the
    ///     leases of a renamed scope to the new name, but a lease written under the old name
    ///     after that move's read would stay there. After each lease write this method reads the
    ///     scope's current name from the database; when it differs from the row's, the row is
    ///     corrected and saved again. If the rename commits after this check, the controller's
    ///     second pass moves the row instead. The request's own scope object keeps its stale
    ///     name; only the row changes. A scope deleted meanwhile leaves the row as it is.
    /// </summary>
    private async Task FollowScopeRenameAsync(
        DhcpScopeEntity scope,
        LeaseEntity lease,
        CancellationToken cancellationToken
    )
    {
        string currentName = await scopeRepository.GetCurrentNameAsync(scope.Id, cancellationToken);

        if (
            currentName is null
            || string.Equals(currentName, lease.ScopeName, StringComparison.Ordinal)
        )
            return;

        logger.LogInformation(
            "Moving lease {LeaseId} from scope '{OldName}' to '{NewName}': the scope was renamed while the lease was written.",
            lease.Id,
            lease.ScopeName,
            currentName
        );

        lease.ScopeName = currentName;
        leaseRepository.Update(lease);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

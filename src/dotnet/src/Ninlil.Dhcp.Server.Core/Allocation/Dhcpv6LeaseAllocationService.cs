using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This class implements <see cref="IDhcpv6LeaseAllocationService" /> on the lease and
///     reservation repositories, the counterpart of <see cref="Dhcpv4LeaseAllocationService" />. It
///     keeps one lease row per client and scope. A DECLINE re-keys the client's row to
///     <c>declined:&lt;address&gt;</c> (see <see cref="DhcpDeclinedAddress" />), an <c>Expired</c>
///     row with a future expiry that no client owns, so the address stays blocked for everyone
///     and the decliner's next SOLICIT starts a new row. Another client's stale (released or
///     expired) row at a chosen value is removed on every offer path.
///     <para>
///         <b>Order.</b> An address offer uses the client's enabled reservation (no offer, if
///         another client blocks it); otherwise the address on the client's own row in any state
///         when it is in range and not reserved for another client; otherwise the IAADDR hint from
///         the client's IA_NA when it is free, in range and not reserved; otherwise the first free
///         pool address (see <see cref="Dhcpv6NetworkHelper" /> for the bounded scan). A prefix
///         offer is the same without the reservation step. Delegated prefixes are aligned to
///         <c>DelegatedPrefixLength</c> and never overlap a blocking row of any length: the sticky
///         prefix must be aligned and not on an <c>Expired</c> row, and the IAPREFIX hint is used
///         only when it has the scope's length, is aligned, is a prefix the pool scan could
///         yield and overlaps no blocking row (RFC 8415 §18.3.9: hints are advisory).
///     </para>
///     <para>
///         Address and prefix allocation are separate methods rather than one generic version:
///         addresses are single <see cref="IPAddress" /> values, prefixes are aligned blocks with a
///         <c>PrefixLength</c>, and prefixes have no reservations.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository leases are read and written through.</param>
/// <param name="reservationRepository">The repository reservations are read through.</param>
/// <param name="scopeRepository">
///     The repository the scope's current name is read through after each lease write, so a row
///     follows a scope rename that committed meanwhile (see <c>FollowScopeRenameAsync</c>).
/// </param>
/// <param name="unitOfWork">The unit of work every change is saved through.</param>
/// <param name="options">The server engine configuration (offer hold and quarantine times).</param>
/// <param name="eventPublisher">
///     The publisher a grant (<see cref="ConfirmAddressAsync" />, <see cref="ConfirmPrefixAsync" />),
///     release (<see cref="ReleaseAsync" />) or decline (<see cref="DeclineAddressAsync" />) is
///     reported to as a
///     <see cref="DhcpLeaseChangedEvent" />, as in <c>Dhcpv4LeaseAllocationService</c>.
/// </param>
/// <param name="logger">The logger allocation problems are recorded to.</param>
public sealed class Dhcpv6LeaseAllocationService(
    ILeaseRepository leaseRepository,
    IDhcpReservationRepository reservationRepository,
    IDhcpScopeRepository scopeRepository,
    IUnitOfWork unitOfWork,
    IOptions<DhcpServerOptions> options,
    IDnsServerEventPublisher eventPublisher,
    ILogger<Dhcpv6LeaseAllocationService> logger
) : IDhcpv6LeaseAllocationService
{
    /// <inheritdoc />
    public async Task<LeaseEntity> OfferAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses association,
        CancellationToken cancellationToken = default
    )
    {
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

        // Order, the same as DHCPv4: the client's reservation, the address it already has, the
        // IAADDR hint (RFC 8415 Section 18.3.9 lets a server use it), then the first free pool
        // address. RFC 8415 Section 13.1 leaves the choice to the administrator's policy.
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
                association.Addresses.FirstOrDefault()?.Address,
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
        await RemoveOccupyingLeaseIfAnyAsync(
            leases,
            candidate.ToString(),
            existing,
            cancellationToken
        );

        LeaseEntity lease = existing ?? NewLease(scope, clientKey);
        lease.IpAddress = candidate.ToString();
        lease.PrefixLength = null;
        lease.State = LeaseStateEnum.Offered;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.OfferHoldDuration);

        await SaveAsync(scope, lease, isNewLease, now, cancellationToken);
        return lease;
    }

    /// <inheritdoc />
    public async Task<LeaseEntity> ConfirmAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress address,
        Dhcpv6LeaseTimes leaseTimes,
        string hostName,
        CancellationToken cancellationToken = default
    )
    {
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

        if (reservation is { IsDisabled: false } && !addressMatchesOwnReservation)
            return null;

        if (!addressMatchesOwnReservation)
        {
            // A reserved address may lie outside the dynamic range, so only other addresses are
            // range-checked.
            if (!IsWithinScopeRange(scope, address))
                return null;

            DhcpReservationEntity addressReservation =
                await reservationRepository.FindByIpAddressAsync(
                    scope.Id,
                    address.ToString(),
                    cancellationToken
                );

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
        // address stays blocked for it and the caller answers NoBinding.
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
                return null;

            // A non-blocking row of another client is deleted first, in its own SaveChanges, so
            // claiming the address does not hit the (ScopeName, IpAddress) unique index.
            leaseRepository.Remove(existingByAddress);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        bool isNewLease = existingByClient is null;
        LeaseEntity lease = existingByClient ?? NewLease(scope, clientKey);

        lease.IpAddress = address.ToString();
        lease.PrefixLength = null;
        lease.State = LeaseStateEnum.Active;
        lease.LeaseObtainedAtUtc ??= now;
        lease.LeaseExpiresAtUtc = now.Add(leaseTimes.Valid);

        if (!string.IsNullOrWhiteSpace(hostName))
            lease.HostName = hostName;

        await SaveAsync(scope, lease, isNewLease, now, cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Granted)
        );

        return lease;
    }

    /// <inheritdoc />
    public async Task<bool> DeclineAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress declinedAddress,
        CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        LeaseEntity lease = await leaseRepository.FindByIpAddressAsync(
            scope.Name,
            declinedAddress.ToString(),
            cancellationToken
        );

        // Only the client's own row may be quarantined; a missing row is not created, or one
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
            return false;
        }

        // The row is re-keyed so no client owns it: the address stays blocked for everyone and
        // the declining client's slot is free for its next SOLICIT.
        lease.ClientIdentifier = DhcpDeclinedAddress.KeyFor(declinedAddress);
        lease.HostName = null;
        lease.State = LeaseStateEnum.Expired;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.DeclinedAddressQuarantineDuration);

        await SaveAsync(scope, lease, isNewLease: false, now, cancellationToken);

        // The row may have been Active, so its DNS record and client identity go with it.
        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Expired)
        );

        logger.LogWarning(
            "{Address} was declined by {ClientIdentifier} in scope '{ScopeName}'; quarantining it until {QuarantineUntilUtc:o}.",
            declinedAddress,
            clientKey,
            scope.Name,
            lease.LeaseExpiresAtUtc
        );
        return true;
    }

    /// <inheritdoc />
    public async Task<LeaseEntity> OfferPrefixAsync(
        DhcpScopeEntity scope,
        string clientKey,
        Dhcpv6IdentityAssociationForPrefixDelegation association,
        CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListByScopeNameAsync(
            scope.Name,
            cancellationToken
        );
        LeaseEntity existing = leases.FirstOrDefault(lease =>
            Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
        );

        // A prefix cannot be declined, so a quarantine-shaped row on the client's key should not
        // exist. It is moved off the client anyway, so it cannot be reused as a prefix lease.
        if (
            existing is not null
            && await DetachQuarantineAsync(scope, existing, now, cancellationToken)
        )
            existing = null;

        IPAddress candidate;

        if (
            existing is { State: not LeaseStateEnum.Expired }
            && existing.PrefixLength == scope.DelegatedPrefixLength
            && IPAddress.TryParse(existing.IpAddress, out IPAddress existingPrefix)
            && IsWithinPrefixPool(scope, existingPrefix)
            && Dhcpv6NetworkHelper.IsAlignedPrefix(
                existingPrefix,
                scope.DelegatedPrefixLength.Value
            )
        )
            candidate = existingPrefix;
        else
            candidate = await ResolveCandidatePrefixAsync(
                scope,
                association.Prefixes.FirstOrDefault(),
                leases,
                existing,
                cancellationToken
            );

        if (candidate is null)
        {
            logger.LogWarning(
                "No prefix is currently available in scope '{ScopeName}' to offer client {ClientIdentifier}.",
                scope.Name,
                clientKey
            );
            return null;
        }

        bool isNewLease = existing is null;

        await RemoveOccupyingLeaseIfAnyAsync(
            leases,
            candidate.ToString(),
            existing,
            cancellationToken
        );

        LeaseEntity lease = existing ?? NewLease(scope, clientKey);
        lease.IpAddress = candidate.ToString();
        lease.PrefixLength = scope.DelegatedPrefixLength;
        lease.State = LeaseStateEnum.Offered;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.OfferHoldDuration);

        await SaveAsync(scope, lease, isNewLease, now, cancellationToken);
        return lease;
    }

    /// <inheritdoc />
    public async Task<LeaseEntity> ConfirmPrefixAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress prefix,
        int prefixLength,
        Dhcpv6LeaseTimes leaseTimes,
        CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (
            !IsWithinPrefixPool(scope, prefix)
            || prefixLength != scope.DelegatedPrefixLength
            || !Dhcpv6NetworkHelper.IsAlignedPrefix(prefix, prefixLength)
        )
            return null;

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListByScopeNameAsync(
            scope.Name,
            cancellationToken
        );
        LeaseEntity existingByClient = leases.FirstOrDefault(lease =>
            Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
        );

        // A blocking prefix row of another client, of any length, that overlaps the confirmed
        // prefix refuses it (changing the scope's DelegatedPrefixLength can leave such a row).
        if (
            leases.Any(lease =>
                !ReferenceEquals(lease, existingByClient)
                && PrefixRowOverlaps(lease, prefix, prefixLength)
                && IsLeaseBlocking(lease, now)
            )
        )
            return null;

        // The unique index is on (ScopeName, IpAddress), so a non-blocking row holding exactly
        // this base is removed whatever its length.
        LeaseEntity existingByPrefix = leases.FirstOrDefault(lease =>
            string.Equals(lease.IpAddress, prefix.ToString(), StringComparison.OrdinalIgnoreCase)
        );

        if (existingByPrefix is not null && !ReferenceEquals(existingByPrefix, existingByClient))
        {
            if (IsLeaseBlocking(existingByPrefix, now))
                return null;

            leaseRepository.Remove(existingByPrefix);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        bool isNewLease = existingByClient is null;
        LeaseEntity lease = existingByClient ?? NewLease(scope, clientKey);

        lease.IpAddress = prefix.ToString();
        lease.PrefixLength = prefixLength;
        lease.State = LeaseStateEnum.Active;
        lease.LeaseObtainedAtUtc ??= now;
        lease.LeaseExpiresAtUtc = now.Add(leaseTimes.Valid);

        await SaveAsync(scope, lease, isNewLease, now, cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Granted)
        );

        return lease;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress value,
        int? prefixLength,
        CancellationToken cancellationToken = default
    )
    {
        LeaseEntity lease = await leaseRepository.FindByClientIdentifierAsync(
            scope.Name,
            clientKey,
            cancellationToken
        );

        if (
            lease is null
            || !string.Equals(lease.IpAddress, value.ToString(), StringComparison.OrdinalIgnoreCase)
            || lease.PrefixLength != prefixLength
        )
            return false;

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

        return true;
    }

    /// <summary>
    ///     This method returns <paramref name="reservation" /> when its address is an
    ///     IPv6 address, and <see langword="null" /> (with a Warning) when it is not, so the
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
            || reserved.AddressFamily == AddressFamily.InterNetworkV6
        )
            return reservation;

        logger.LogWarning(
            "The reservation for {ClientIdentifier} in scope '{ScopeName}' ({Address}) is not an IPv6 address, so it is skipped and the client is treated as having no reservation. Correct or delete the reservation.",
            reservation.ClientIdentifier,
            scope.Name,
            reservation.IpAddress
        );
        return null;
    }

    /// <summary>
    ///     This method looks up the client's enabled reservation. A reservation outranks the
    ///     client's current binding, and <see cref="ConfirmAddressAsync" /> refuses every other
    ///     address, so a conflict gives no offer at all.
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
    ///     This method returns the address on the client's own row when the client may keep it:
    ///     in range and not reserved for another client. The row's state does not matter; a row
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
            existing.PrefixLength is not null
            || !IPAddress.TryParse(existing.IpAddress, out IPAddress existingAddress)
            || !IsWithinScopeRange(scope, existingAddress)
        )
            return null;

        // The client's own enabled reservation was handled first, so one found here is another
        // client's and ConfirmAddressAsync would refuse the address.
        DhcpReservationEntity addressReservation = await reservationRepository.FindByIpAddressAsync(
            scope.Id,
            existingAddress.ToString(),
            cancellationToken
        );

        return addressReservation is { IsDisabled: false } ? null : existingAddress;
    }

    /// <summary>
    ///     This method chooses an address for a client with no usable reservation and no address
    ///     it can keep: the IAADDR hint from the client's IA_NA when it is free, in range and not
    ///     reserved, otherwise the first free pool address. The client's own row never blocks.
    /// </summary>
    private async Task<IPAddress> ResolveCandidateAddressAsync(
        DhcpScopeEntity scope,
        IPAddress hint,
        LeaseEntity own,
        IReadOnlyList<LeaseEntity> leases,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // The IAADDR hint from the client's IA_NA, if free, in range and not reserved.
        if (hint is not null && IsWithinScopeRange(scope, hint))
        {
            DhcpReservationEntity hintReservation =
                await reservationRepository.FindByIpAddressAsync(
                    scope.Id,
                    hint.ToString(),
                    cancellationToken
                );

            if (hintReservation is not { IsDisabled: false })
            {
                LeaseEntity conflicting = leases.FirstOrDefault(lease =>
                    !ReferenceEquals(lease, own)
                    && string.Equals(
                        lease.IpAddress,
                        hint.ToString(),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (conflicting is null || !IsLeaseBlocking(conflicting, now))
                    return hint;
            }
        }

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

        foreach (
            IPAddress candidate in Dhcpv6NetworkHelper.EnumerateAddresses(rangeStart, rangeEnd)
        )
        {
            string candidateKey = candidate.ToString();

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
    ///     This method chooses a prefix for a client that cannot keep its current one: the
    ///     IAPREFIX hint when it is usable (see <see cref="IsUsablePrefixHint" />), otherwise the
    ///     first aligned prefix in the pool scan that overlaps no blocking row. The client's own
    ///     row (<paramref name="own" />) never blocks, because the offer overwrites it.
    /// </summary>
    // Returns a Task without being async because it does no I/O: the lease list is already loaded.
    private Task<IPAddress> ResolveCandidatePrefixAsync(
        DhcpScopeEntity scope,
        Dhcpv6IdentityAssociationPrefix hint,
        IReadOnlyList<LeaseEntity> leases,
        LeaseEntity own,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (
            scope.DelegatedPrefixLength is not { } delegatedPrefixLength
            || !IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
            || !IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        )
        {
            logger.LogWarning(
                "Scope '{ScopeName}' has an invalid prefix pool ({RangeStart}-{RangeEnd}, delegated length {DelegatedPrefixLength}) and cannot allocate any prefix.",
                scope.Name,
                scope.RangeStartAddress,
                scope.RangeEndAddress,
                scope.DelegatedPrefixLength
            );
            return Task.FromResult<IPAddress>(null);
        }

        List<LeaseEntity> blocking = leases
            .Where(lease =>
                !ReferenceEquals(lease, own)
                && lease.PrefixLength is not null
                && IsLeaseBlocking(lease, now)
            )
            .ToList();

        bool Free(IPAddress candidate) =>
            !blocking.Any(lease => PrefixRowOverlaps(lease, candidate, delegatedPrefixLength));

        if (
            IsUsablePrefixHint(hint, delegatedPrefixLength, rangeStart, rangeEnd)
            && Free(hint.Prefix)
        )
            return Task.FromResult(hint.Prefix);

        foreach (
            IPAddress candidate in Dhcpv6NetworkHelper.EnumeratePrefixes(
                rangeStart,
                rangeEnd,
                delegatedPrefixLength
            )
        )
        {
            if (Free(candidate))
                return Task.FromResult(candidate);
        }

        return Task.FromResult<IPAddress>(null);
    }

    /// <summary>
    ///     This method reports whether an IAPREFIX hint can be granted as is: it has the scope's
    ///     delegated length, is aligned, and is a base the pool scan would yield (at or above the
    ///     range start rounded down to a prefix boundary, and not above the range end).
    /// </summary>
    private static bool IsUsablePrefixHint(
        Dhcpv6IdentityAssociationPrefix hint,
        int delegatedPrefixLength,
        IPAddress rangeStart,
        IPAddress rangeEnd
    )
    {
        if (
            hint?.Prefix is null
            || hint.PrefixLength != delegatedPrefixLength
            || !Dhcpv6NetworkHelper.IsAlignedPrefix(hint.Prefix, delegatedPrefixLength)
        )
            return false;

        BigInteger value = Dhcpv6NetworkHelper.ToBigInteger(hint.Prefix);
        BigInteger firstBase =
            Dhcpv6NetworkHelper.ToBigInteger(rangeStart)
            & Dhcpv6NetworkHelper.ComputeMask(delegatedPrefixLength);

        return value >= firstBase && value <= Dhcpv6NetworkHelper.ToBigInteger(rangeEnd);
    }

    /// <summary>
    ///     This method reports whether a lease row is a prefix row (it has a
    ///     <c>PrefixLength</c> and a parseable address) that overlaps the given prefix.
    /// </summary>
    private static bool PrefixRowOverlaps(LeaseEntity lease, IPAddress prefix, int prefixLength) =>
        lease.PrefixLength is { } rowLength
        && IPAddress.TryParse(lease.IpAddress, out IPAddress rowPrefix)
        && Dhcpv6NetworkHelper.PrefixesOverlap(rowPrefix, rowLength, prefix, prefixLength);

    /// <summary>
    ///     This method deletes, in its own <c>SaveChanges</c>, another client's non-blocking row
    ///     that holds a value the client's row is about to claim (the client's own row, <paramref name="own" />,
    ///     is never removed), so the save does not hit the
    ///     <c>(ScopeName, IpAddress)</c> unique index. The match ignores <c>PrefixLength</c>
    ///     because the index does not include it (see <c>Dhcpv4LeaseAllocationService</c>).
    /// </summary>
    private async Task RemoveOccupyingLeaseIfAnyAsync(
        IReadOnlyList<LeaseEntity> leases,
        string value,
        LeaseEntity own,
        CancellationToken cancellationToken
    )
    {
        LeaseEntity occupying = leases.FirstOrDefault(lease =>
            !ReferenceEquals(lease, own)
            && string.Equals(lease.IpAddress, value, StringComparison.OrdinalIgnoreCase)
        );

        if (occupying is null)
            return;

        leaseRepository.Remove(occupying);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
    ///     This method reports whether a lease row makes its value unavailable, by the rule of
    ///     <c>Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>.
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
        && Dhcpv6NetworkHelper.IsWithinRange(address, rangeStart, rangeEnd);

    /// <summary>
    ///     This method reports whether a prefix base address is within the pool range. It does
    ///     not check alignment; callers pair it with
    ///     <see cref="Dhcpv6NetworkHelper.IsAlignedPrefix" />.
    /// </summary>
    private static bool IsWithinPrefixPool(DhcpScopeEntity scope, IPAddress prefix) =>
        IsWithinScopeRange(scope, prefix);

    /// <summary>
    ///     This method creates a new IPv6 lease entity with its identifier and creation time set,
    ///     as <c>Dhcpv4LeaseAllocationService.NewLease</c> does.
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
            ProtocolVersion = InternetProtocolVersionEnum.IPv6,
            ScopeName = scope.Name,
            ClientIdentifier = clientKey,
            IpAddress = ipAddress?.ToString(),
        };

    /// <summary>
    ///     This method adds or updates a lease and saves, as
    ///     <c>Dhcpv4LeaseAllocationService.SaveAsync</c> does.
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

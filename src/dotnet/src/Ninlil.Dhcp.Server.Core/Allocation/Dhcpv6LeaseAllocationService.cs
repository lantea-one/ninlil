using System.Net;
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
///     This class implements <see cref="IDhcpv6LeaseAllocationService" /> against the real
///     <see cref="ILeaseRepository" />/<see cref="IDhcpReservationRepository" /> repositories --
///     the DHCPv6 analogue of <see cref="Dhcpv4LeaseAllocationService" />, following the exact
///     same "one lease row per (scope, client), reused across its whole lifecycle" and
///     "represent a quarantine via the shared <c>Expired</c> state" design notes that class's own
///     doc comment documents -- see that class's own doc comment for the reasoning, not repeated
///     here.
///     <para>
///         Address (IA_NA) and delegated-prefix (IA_PD) allocation share the exact same shape --
///         sticky reuse of an already-held value, then a hint, then a pool scan -- but are kept as
///         separate method pairs rather than one generic implementation: an address's own
///         candidate space is a flat range of individual <see cref="IPAddress" />s, while a
///         prefix's own candidate space is a range of block-aligned prefix bases each carrying an
///         extra <c>PrefixLength</c>, and a prefix additionally has no reservation lookup at all
///         (see <see cref="IDhcpv6LeaseAllocationService" />'s own doc comment) -- forcing a single
///         shared abstraction across both would cost more real clarity than the modest code
///         duplication it would save, the same reasoning this project's own DHCPv4/DHCPv6 split
///         itself is built on.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository this service reads and writes leases through.</param>
/// <param name="reservationRepository">The repository this service reads static reservations through.</param>
/// <param name="unitOfWork">The unit of work this service commits every change through.</param>
/// <param name="options">This server's own core configuration.</param>
/// <param name="eventPublisher">
///     The publisher a real grant (<see cref="ConfirmAddressAsync" />/<see cref="ConfirmPrefixAsync" />)
///     or release (<see cref="ReleaseAsync" />) reports through, via
///     <see cref="DhcpLeaseChangedEvent" /> -- mirrors <c>Dhcpv4LeaseAllocationService</c>'s own
///     publish points exactly.
/// </param>
/// <param name="logger">The logger this service's own allocation decisions are recorded to.</param>
public sealed class Dhcpv6LeaseAllocationService(
    ILeaseRepository leaseRepository,
    IDhcpReservationRepository reservationRepository,
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

        IPAddress candidate;

        // Sticky reuse -- see Dhcpv4LeaseAllocationService.OfferAsync's own doc comment for why
        // an Expired row (this engine's own decline-quarantine sentinel) is deliberately excluded.
        if (
            existing is { State: not LeaseStateEnum.Expired }
            && IPAddress.TryParse(existing.IpAddress, out IPAddress existingAddress)
            && IsWithinScopeRange(scope, existingAddress)
        )
            candidate = existingAddress;
        else
            candidate = await ResolveCandidateAddressAsync(
                scope,
                clientKey,
                association.Addresses.FirstOrDefault()?.Address,
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
            await RemoveOccupyingLeaseIfAnyAsync(
                leases,
                candidate.ToString(),
                null,
                cancellationToken
            );

        LeaseEntity lease = existing ?? NewLease(scope, clientKey);
        lease.IpAddress = candidate.ToString();
        lease.PrefixLength = null;
        lease.State = LeaseStateEnum.Offered;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.OfferHoldDuration);

        await SaveAsync(lease, isNewLease, now, cancellationToken);
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
        LeaseEntity existingByAddress = leases.FirstOrDefault(lease =>
            string.Equals(lease.IpAddress, address.ToString(), StringComparison.OrdinalIgnoreCase)
        );

        if (existingByAddress is not null && !ReferenceEquals(existingByAddress, existingByClient))
        {
            if (IsLeaseBlocking(existingByAddress, now))
                return null;

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

        await SaveAsync(lease, isNewLease, now, cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Granted)
        );

        return lease;
    }

    /// <inheritdoc />
    public async Task DeclineAddressAsync(
        DhcpScopeEntity scope,
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
        bool isNewLease = lease is null;
        lease ??= NewLease(scope, clientKey: null, ipAddress: declinedAddress);

        lease.State = LeaseStateEnum.Expired;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.DeclinedAddressQuarantineDuration);

        await SaveAsync(lease, isNewLease, now, cancellationToken);

        logger.LogWarning(
            "{Address} was declined in scope '{ScopeName}'; quarantining it until {QuarantineUntilUtc:o}.",
            declinedAddress,
            scope.Name,
            lease.LeaseExpiresAtUtc
        );
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

        IPAddress candidate;

        if (
            existing is { State: not LeaseStateEnum.Expired }
            && existing.PrefixLength == scope.DelegatedPrefixLength
            && IPAddress.TryParse(existing.IpAddress, out IPAddress existingPrefix)
            && IsWithinPrefixPool(scope, existingPrefix)
        )
            candidate = existingPrefix;
        else
            candidate = await ResolveCandidatePrefixAsync(
                scope,
                association.Prefixes.FirstOrDefault()?.Prefix,
                leases,
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

        if (isNewLease)
            await RemoveOccupyingLeaseIfAnyAsync(
                leases,
                candidate.ToString(),
                scope.DelegatedPrefixLength,
                cancellationToken
            );

        LeaseEntity lease = existing ?? NewLease(scope, clientKey);
        lease.IpAddress = candidate.ToString();
        lease.PrefixLength = scope.DelegatedPrefixLength;
        lease.State = LeaseStateEnum.Offered;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.OfferHoldDuration);

        await SaveAsync(lease, isNewLease, now, cancellationToken);
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

        if (!IsWithinPrefixPool(scope, prefix) || prefixLength != scope.DelegatedPrefixLength)
            return null;

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListByScopeNameAsync(
            scope.Name,
            cancellationToken
        );
        LeaseEntity existingByClient = leases.FirstOrDefault(lease =>
            Dhcpv4ClientIdentifier.AreEqual(lease.ClientIdentifier, clientKey)
        );
        LeaseEntity existingByPrefix = leases.FirstOrDefault(lease =>
            string.Equals(lease.IpAddress, prefix.ToString(), StringComparison.OrdinalIgnoreCase)
            && lease.PrefixLength == prefixLength
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

        await SaveAsync(lease, isNewLease, now, cancellationToken);

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

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Released)
        );

        return true;
    }

    /// <summary>
    ///     This method resolves the address to offer a client with no already-held, still-in-range
    ///     address of its own -- the DHCPv6 analogue of
    ///     <c>Dhcpv4LeaseAllocationService.ResolveCandidateAddressAsync</c>.
    /// </summary>
    private async Task<IPAddress> ResolveCandidateAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress hint,
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

        // The client's own hinted address (an IAADDR it already included in its own IA_NA),
        // honored if it is free, in range, and not reserved to someone else.
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
                    string.Equals(
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

        Dictionary<string, LeaseEntity> leasesByAddress = leases.ToDictionary(
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
    ///     This method resolves the prefix to offer a client with no already-held, still-in-pool
    ///     prefix of its own -- the Prefix Delegation analogue of
    ///     <see cref="ResolveCandidateAddressAsync" />, minus any reservation lookup.
    /// </summary>
    private Task<IPAddress> ResolveCandidatePrefixAsync(
        DhcpScopeEntity scope,
        IPAddress hint,
        IReadOnlyList<LeaseEntity> leases,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (hint is not null && IsWithinPrefixPool(scope, hint))
        {
            LeaseEntity conflicting = leases.FirstOrDefault(lease =>
                string.Equals(lease.IpAddress, hint.ToString(), StringComparison.OrdinalIgnoreCase)
                && lease.PrefixLength == scope.DelegatedPrefixLength
            );

            if (conflicting is null || !IsLeaseBlocking(conflicting, now))
                return Task.FromResult(hint);
        }

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

        Dictionary<string, LeaseEntity> leasesByPrefix = leases
            .Where(lease => lease.PrefixLength == delegatedPrefixLength)
            .ToDictionary(
                lease => lease.IpAddress,
                lease => lease,
                StringComparer.OrdinalIgnoreCase
            );

        foreach (
            IPAddress candidate in Dhcpv6NetworkHelper.EnumeratePrefixes(
                rangeStart,
                rangeEnd,
                delegatedPrefixLength
            )
        )
        {
            string candidateKey = candidate.ToString();

            if (
                leasesByPrefix.TryGetValue(candidateKey, out LeaseEntity occupyingLease)
                && IsLeaseBlocking(occupyingLease, now)
            )
                continue;

            return Task.FromResult(candidate);
        }

        return Task.FromResult<IPAddress>(null);
    }

    /// <summary>
    ///     This method removes a stale, non-blocking row for a *different* client already
    ///     occupying a value about to be claimed by a brand-new row, in its own <c>SaveChanges</c>
    ///     -- see <c>Dhcpv4LeaseAllocationService.OfferAsync</c>'s own doc comment for why this
    ///     must happen before the new row is saved, to never race the same
    ///     <c>(ScopeName, IpAddress)</c> unique index within one transaction.
    /// </summary>
    private async Task RemoveOccupyingLeaseIfAnyAsync(
        IReadOnlyList<LeaseEntity> leases,
        string value,
        int? prefixLength,
        CancellationToken cancellationToken
    )
    {
        LeaseEntity occupying = leases.FirstOrDefault(lease =>
            string.Equals(lease.IpAddress, value, StringComparison.OrdinalIgnoreCase)
            && lease.PrefixLength == prefixLength
        );

        if (occupying is null)
            return;

        leaseRepository.Remove(occupying);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     This method reports whether an address's own existing lease row currently blocks it
    ///     from being (re)allocated -- see <c>Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>'s
    ///     own doc comment.
    /// </summary>
    private static bool IsLeaseBlocking(LeaseEntity lease, DateTimeOffset now) =>
        lease.State != LeaseStateEnum.Released
        && (!lease.LeaseExpiresAtUtc.HasValue || lease.LeaseExpiresAtUtc.Value > now);

    /// <summary>
    ///     This method reports whether an address falls within an address-assignment scope's own
    ///     configured range.
    /// </summary>
    private static bool IsWithinScopeRange(DhcpScopeEntity scope, IPAddress address) =>
        IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
        && IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        && Dhcpv6NetworkHelper.IsWithinRange(address, rangeStart, rangeEnd);

    /// <summary>
    ///     This method reports whether a prefix falls within a Prefix Delegation scope's own
    ///     configured pool range.
    /// </summary>
    private static bool IsWithinPrefixPool(DhcpScopeEntity scope, IPAddress prefix) =>
        IsWithinScopeRange(scope, prefix);

    /// <summary>
    ///     This method constructs a brand-new lease entity with its own identifier and creation
    ///     timestamp already assigned -- see <c>Dhcpv4LeaseAllocationService.NewLease</c>'s own
    ///     doc comment for the underlying "caller assigns the identifier" convention.
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
    ///     This method stages and commits a lease's own insert or update -- see
    ///     <c>Dhcpv4LeaseAllocationService.SaveAsync</c>'s own doc comment.
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

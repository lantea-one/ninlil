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
///     This class implements <see cref="IDhcpv6LeaseAllocationService" /> on the lease and
///     reservation repositories, the counterpart of <see cref="Dhcpv4LeaseAllocationService" />. It
///     keeps one lease row per client and scope and stores a DECLINE quarantine as an
///     <c>Expired</c> row with a future expiry, as described there.
///     <para>
///         <b>Order.</b> An address offer uses the client's existing row when it is not
///         <c>Expired</c> and still in range; otherwise the client's enabled reservation (none,
///         if another client blocks it); otherwise the IAADDR hint from the client's IA_NA when it
///         is free, in range and not reserved; otherwise the first free pool address (see
///         <see cref="Dhcpv6NetworkHelper" /> for the bounded scan). A prefix offer is the same
///         without the reservation step, and the hint is checked only for being inside the
///         pool range.
///     </para>
///     <para>
///         Address and prefix allocation are separate methods rather than one generic version:
///         addresses are single <see cref="IPAddress" /> values, prefixes are aligned blocks with a
///         <c>PrefixLength</c>, and prefixes have no reservations.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository leases are read and written through.</param>
/// <param name="reservationRepository">The repository reservations are read through.</param>
/// <param name="unitOfWork">The unit of work every change is saved through.</param>
/// <param name="options">The server engine configuration (offer hold and quarantine times).</param>
/// <param name="eventPublisher">
///     The publisher a grant (<see cref="ConfirmAddressAsync" />, <see cref="ConfirmPrefixAsync" />)
///     or release (<see cref="ReleaseAsync" />) is reported to as a
///     <see cref="DhcpLeaseChangedEvent" />, as in <c>Dhcpv4LeaseAllocationService</c>.
/// </param>
/// <param name="logger">The logger allocation problems are recorded to.</param>
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

        // Sticky reuse, excluding an Expired row (a decline quarantine), as in
        // Dhcpv4LeaseAllocationService.OfferAsync.
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

        lease.State = LeaseStateEnum.Expired;
        lease.LeaseExpiresAtUtc = now.Add(options.Value.DeclinedAddressQuarantineDuration);

        await SaveAsync(lease, isNewLease: false, now, cancellationToken);

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
    ///     This method chooses an address for a client that cannot keep its current one, like
    ///     <c>Dhcpv4LeaseAllocationService.ResolveCandidateAddressAsync</c> but with the IAADDR
    ///     hint in place of the Requested IP Address option.
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
    ///     This method chooses a prefix for a client that cannot keep its current one: the
    ///     IAPREFIX hint when its address is in the pool range and no blocking lease holds it
    ///     (alignment is not checked), otherwise the first free prefix in the pool scan.
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
    ///     This method deletes, in its own <c>SaveChanges</c>, another client's non-blocking row
    ///     that holds a value a new row is about to claim, so the insert does not hit the
    ///     <c>(ScopeName, IpAddress)</c> unique index (see <c>Dhcpv4LeaseAllocationService</c>).
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
    ///     not check alignment.
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

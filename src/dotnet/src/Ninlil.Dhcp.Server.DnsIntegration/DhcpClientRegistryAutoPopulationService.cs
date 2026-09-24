using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Common.Identity;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class auto-populates Ninlil's own "known client" registry
///     (<see cref="DnsClientEntity" />/<see cref="IDnsClientRepository" />) the first time a DHCP
///     lease is granted for a client identifier the registry has never seen before -- a sibling
///     reaction to <see cref="DhcpDnsRegistrationService" />'s own A/AAAA registration, not a
///     replacement for it: this service runs regardless of whether the lease's own owning
///     <see cref="DhcpScopeEntity" /> has DHCP-&gt;DNS integration configured at all
///     (<see cref="DhcpScopeEntity.DnsZoneId" /> unset), since a known-client row is useful on its
///     own even when no A/AAAA record is ever registered for it.
///     <para>
///         <b>An address row is created at most once per client identifier, never overwritten on a
///         later renewal.</b> Once a <see cref="DnsClientAddressEntity" /> row exists for a given
///         identifier/protocol pair, this service leaves its owning device's own
///         <see cref="DnsClientEntity.DisplayName" /> alone forever afterward -- mirroring
///         <see cref="DhcpReservationEntity.HostName" />'s own established "operator-supplied,
///         independent of any later client-presented name" precedent exactly. An operator may have
///         since renamed the client to something more meaningful than whatever host name it
///         happened to present; a later DHCP renewal must never silently revert that.
///     </para>
///     <para>
///         <b>A previously-unseen identifier joins an existing device when it provably belongs to
///         it, and creates a new device otherwise.</b> A dual-stack device presents a MAC-based
///         identifier to DHCPv4 and a DUID to DHCPv6; <see cref="DhcpClientIdentifierLinkKeys" />
///         recovers the MAC a DUID-LLT/DUID-LL embeds, and the DUID an RFC 4361 DHCPv4
///         Client-identifier embeds, so the two can be recognized as one. When exactly one existing
///         device already answers to an identifier sharing such a link key, the new identifier is
///         added to that device (seeding its <see cref="DnsClientEntity.DisplayName" /> only if it
///         has none yet). No match, or matches on more than one device, creates a new device as
///         before -- an ambiguous merge is never guessed, and an operator can still link two
///         identifiers by hand through the device's own address collection. Candidates are found
///         by scanning every stored address in memory, which is fine at this project's homelab
///         scale and runs only the first time an identifier is ever seen.
///     </para>
///     <para>
///         <b>Only a real lease grant creates a row.</b> A release or expiry is deliberately a
///         no-op here -- unlike <see cref="DhcpDnsRegistrationService" />, which must remove its
///         own A/AAAA record the moment a lease is no longer valid, the known-client registry
///         describes a physical device, not a currently-active address binding, so the row this
///         service created for that device is left alone even after its lease lapses.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository used to re-read the lease this event names.</param>
/// <param name="clientRepository">The repository this service reads and writes known devices through.</param>
/// <param name="clientAddressRepository">The repository this service reads and writes a device's own addresses through.</param>
/// <param name="unitOfWork">The unit of work a new row is committed through.</param>
/// <param name="logger">The logger this service records skip decisions to.</param>
/// <param name="presenceRecorder">The presence recorder every grant or renewal is reported to as a sighting, or <see langword="null" /> when presence tracking is not registered.</param>
public class DhcpClientRegistryAutoPopulationService(
    ILeaseRepository leaseRepository,
    IDnsClientRepository clientRepository,
    IDnsClientAddressRepository clientAddressRepository,
    IUnitOfWork unitOfWork,
    ILogger<DhcpClientRegistryAutoPopulationService> logger,
    IClientPresenceRecorder presenceRecorder = null
)
{
    /// <summary>
    ///     This method reacts to a single <c>DhcpLeaseChangedEvent</c>, re-reading the lease it
    ///     names fresh (the event itself is id-only) and creating a known-client row for it if,
    ///     and only if, the change is a real grant and no row for this identifier exists yet.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease that changed.</param>
    /// <param name="changeKind">What actually happened to the lease.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> if a new row was created, <see langword="false" /> otherwise.</returns>
    public async Task<bool> HandleLeaseChangeAsync(
        Guid leaseId,
        DhcpLeaseChangeKindEnum changeKind,
        CancellationToken stoppingToken = default
    )
    {
        // A release or expiry never creates or touches a known-client row -- see this class's
        // own doc comment for why.
        if (changeKind != DhcpLeaseChangeKindEnum.Granted)
            return false;

        LeaseEntity lease = await leaseRepository.GetByIdAsync(leaseId, stoppingToken);
        if (lease is null)
        {
            logger.LogDebug(
                "Skipping known-client auto-population for lease {LeaseId} -- the lease no longer exists.",
                leaseId
            );
            return false;
        }

        // Every grant -- including a renewal for an already-known device -- is proof the client
        // is present right now. A Prefix Delegation lease's own address is a delegated prefix,
        // not the client's address, so only its identity is recorded.
        presenceRecorder?.RecordIdentity(
            lease.ClientIdentifier,
            lease.ProtocolVersion,
            lease.PrefixLength is null && IPAddress.TryParse(lease.IpAddress, out IPAddress ip)
                ? ip
                : null,
            DateTimeOffset.UtcNow
        );

        DnsClientEntity existing = await clientRepository.GetByClientIdentifierAsync(
            lease.ClientIdentifier,
            lease.ProtocolVersion,
            stoppingToken
        );

        // A device already answers to this identifier -- never overwrite its own DisplayName, and
        // nothing else about a known-client row is derived from a lease at all.
        if (existing is not null)
            return false;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string hostName = string.IsNullOrWhiteSpace(lease.HostName) ? null : lease.HostName.Trim();

        // A dual-stack device's other identifier may already be registered -- if exactly one
        // device provably owns it, this identifier joins that device instead of creating a
        // duplicate. See this class's own doc comment.
        DnsClientEntity sibling = await FindLinkedDeviceAsync(lease, stoppingToken);
        if (sibling is not null)
        {
            await clientAddressRepository.AddAsync(
                NewAddress(sibling.Id, lease, now),
                stoppingToken
            );

            // Only fill a name that was never set -- an existing name is never overwritten.
            if (sibling.DisplayName is null && hostName is not null)
            {
                sibling.DisplayName = hostName;
                clientRepository.Update(sibling);
            }

            await unitOfWork.SaveChangesAsync(stoppingToken);
            logger.LogInformation(
                "Linked {ProtocolVersion} identifier {ClientIdentifier} to existing client {ClientId}, which already answers to an identifier for the same hardware.",
                lease.ProtocolVersion,
                lease.ClientIdentifier,
                sibling.Id
            );
            return true;
        }

        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = hostName,
            Origin = DnsClientOriginEnum.Dhcp,
            CreatedAtUtc = now,
        };

        DnsClientAddressEntity address = NewAddress(client.Id, lease, now);

        await clientRepository.AddAsync(client, stoppingToken);
        await clientAddressRepository.AddAsync(address, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return true;
    }

    /// <summary>
    ///     This method finds the one existing device that already answers to an identifier
    ///     sharing a link key with this lease's own identifier, or <see langword="null" /> when
    ///     no device does, or more than one does.
    /// </summary>
    /// <param name="lease">The lease whose identifier has never been seen before.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The single linked device, or <see langword="null" />.</returns>
    private async Task<DnsClientEntity> FindLinkedDeviceAsync(
        LeaseEntity lease,
        CancellationToken stoppingToken
    )
    {
        IReadOnlySet<string> leaseKeys = LinkKeysFor(lease.ClientIdentifier, lease.ProtocolVersion);
        if (leaseKeys.Count == 0)
            return null;

        List<Guid> linkedDeviceIds =
        [
            .. (await clientAddressRepository.ListAsync(stoppingToken: stoppingToken))
                .Where(address =>
                    LinkKeysFor(address.ClientIdentifier, address.ProtocolVersion)
                        .Overlaps(leaseKeys)
                )
                .Select(address => address.ClientId)
                .Distinct(),
        ];

        if (linkedDeviceIds.Count > 1)
            logger.LogInformation(
                "Not linking {ProtocolVersion} identifier {ClientIdentifier} to an existing client -- it matches {Count} different clients, so the match is ambiguous.",
                lease.ProtocolVersion,
                lease.ClientIdentifier,
                linkedDeviceIds.Count
            );

        return linkedDeviceIds.Count == 1
            ? await clientRepository.GetByIdAsync(linkedDeviceIds[0], stoppingToken)
            : null;
    }

    /// <summary>
    ///     This method derives an identifier's link keys according to its own protocol version.
    /// </summary>
    private static IReadOnlySet<string> LinkKeysFor(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion
    ) =>
        protocolVersion == InternetProtocolVersionEnum.IPv6
            ? DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(clientIdentifier)
            : DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(clientIdentifier);

    /// <summary>
    ///     This method builds the address row recording a lease's own identifier under a device.
    /// </summary>
    private static DnsClientAddressEntity NewAddress(
        Guid clientId,
        LeaseEntity lease,
        DateTimeOffset now
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ClientIdentifier = lease.ClientIdentifier,
            ProtocolVersion = lease.ProtocolVersion,
            FirstSeenUtc = now,
            CreatedAtUtc = now,
        };
}

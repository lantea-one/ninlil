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
///     This class adds DHCP clients to the client registry (<see cref="DnsClientEntity" />,
///     <see cref="IDnsClientRepository" />) the first time a lease is granted for a client
///     identifier the registry has not seen. It reacts to the same events as
///     <see cref="DhcpDnsRegistrationService" /> but independently of it: it runs whether or not
///     the scope has a <see cref="DhcpScopeEntity.DnsZoneId" />, since a registry entry is useful
///     without a DNS record. Every grant is also reported to the presence recorder, if there is
///     one.
///     <para>
///         <b>Created once, never overwritten.</b> Once a <see cref="DnsClientAddressEntity" />
///         exists for an identifier and protocol, later grants leave the device alone, including
///         its <see cref="DnsClientEntity.DisplayName" />, which an operator may have changed, as
///         an operator-supplied <see cref="DhcpReservationEntity.HostName" /> is independent of
///         the client's name.
///     </para>
///     <para>
///         <b>Linking dual-stack devices.</b> A dual-stack device presents a MAC-based identifier
///         to DHCPv4 and a DUID to DHCPv6. <see cref="DhcpClientIdentifierLinkKeys" /> recovers the
///         MAC inside a DUID-LLT or DUID-LL, and the DUID inside an RFC 4361 Client-identifier, so
///         the two can be recognized. When exactly one device already has an identifier sharing a
///         link key, the new identifier is added to it (and its <see cref="DnsClientEntity.DisplayName" />
///         set only if empty). With no match, or matches on several devices, a new device is
///         created; an ambiguous merge is never guessed, and an operator can link identifiers by
///         hand. Candidates are found by loading every stored address, acceptable at home-network
///         scale and only done for a new identifier.
///     </para>
///     <para>
///         <b>Only grants count.</b> Releases and expiries change nothing here: the registry
///         describes devices, not current address bindings, so an entry stays after the lease
///         ends.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository the lease named by the event is read from.</param>
/// <param name="clientRepository">The repository devices are read and written through.</param>
/// <param name="clientAddressRepository">The repository a device's identifiers are read and written through.</param>
/// <param name="unitOfWork">The unit of work new rows are saved through.</param>
/// <param name="logger">The logger skips and links are recorded to.</param>
/// <param name="presenceRecorder">The presence recorder every grant is reported to as a sighting, or <see langword="null" /> when presence tracking is not registered.</param>
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
    ///     This method handles one <c>DhcpLeaseChangedEvent</c>: for a grant it reads the lease
    ///     (the event carries only its identifier), records the sighting, and adds the identifier
    ///     to the registry if it is new, either under a linked device or as a new device.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease that changed.</param>
    /// <param name="changeKind">What happened to the lease.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> if a registry row was added, <see langword="false" /> otherwise.</returns>
    public async Task<bool> HandleLeaseChangeAsync(
        Guid leaseId,
        DhcpLeaseChangeKindEnum changeKind,
        CancellationToken stoppingToken = default
    )
    {
        // Releases and expiries change nothing (see the class summary).
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

        // Every grant, renewals included, shows the client is present. A prefix delegation
        // lease holds a prefix, not the client's address, so only the identity is recorded.
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

        // A device already has this identifier; leave it alone, DisplayName included.
        if (existing is not null)
            return false;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string hostName = string.IsNullOrWhiteSpace(lease.HostName) ? null : lease.HostName.Trim();

        // If exactly one device has a linked identifier (the other side of a dual-stack device),
        // add this identifier to it instead of creating a duplicate.
        DnsClientEntity sibling = await FindLinkedDeviceAsync(lease, stoppingToken);
        if (sibling is not null)
        {
            await clientAddressRepository.AddAsync(
                NewAddress(sibling.Id, lease, now),
                stoppingToken
            );

            // Fill the name only if it was never set.
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
    ///     This method finds the one device with an identifier that shares a link key with the
    ///     lease's identifier, or returns <see langword="null" /> when none or several do.
    /// </summary>
    /// <param name="lease">The lease whose identifier is new to the registry.</param>
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
    ///     This method derives an identifier's link keys for its protocol version.
    /// </summary>
    private static IReadOnlySet<string> LinkKeysFor(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion
    ) =>
        protocolVersion == InternetProtocolVersionEnum.IPv6
            ? DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(clientIdentifier)
            : DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(clientIdentifier);

    /// <summary>
    ///     This method builds the row that records a lease's identifier under a device.
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

using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class rewrites client identifiers stored in another form (such as <c>AA-BB-CC-DD-EE-FF</c>
///     or <c>aabb.ccdd.eeff</c>) into the normal form of <see cref="ClientIdentifierFormat" />
///     (lowercase hex pairs joined by colons). The write APIs normalise on input, and the DHCP
///     engine and the client matcher compare the normal form exactly, so a row stored in another
///     form never matches. <c>Program.cs</c> runs this once per startup after the schema is
///     ready; it does nothing when every row is already in normal form.
///     <para>
///         It covers DHCP reservations (the identifier, plus the address in its canonical
///         <see cref="IPAddress.ToString()" /> form), client addresses and client static address
///         mappings. It goes through the repositories, so one implementation covers SQLite,
///         PostgreSQL, MySQL, SQL Server and MongoDB with no schema change. Rows that would
///         collide after normalisation (the same identifier in one scope for reservations, the same
///         identifier and protocol for client addresses) are left untouched and one warning names
///         each group, because which row wins is the operator's call. A value that does not parse
///         never matched anything; its row is left untouched with a warning.
///     </para>
/// </summary>
public static class StoredClientIdentifierNormalizer
{
    /// <summary>
    ///     This method normalises every stored identifier that can be normalised without a clash.
    /// </summary>
    /// <param name="reservationRepository">Reads and updates the DHCP reservations.</param>
    /// <param name="clientAddressRepository">Reads and updates the client addresses.</param>
    /// <param name="staticMappingRepository">Reads and updates the static address mappings.</param>
    /// <param name="unitOfWork">Commits the changes once, after all three tables are decided.</param>
    /// <param name="logger">Receives the collision and unparseable-value warnings.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of rows changed.</returns>
    public static async Task<int> RunAsync(
        IDhcpReservationRepository reservationRepository,
        IDnsClientAddressRepository clientAddressRepository,
        IDnsClientStaticAddressMappingRepository staticMappingRepository,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken stoppingToken = default
    )
    {
        // Every row is read before any is changed, so the collision decision sees the whole table.
        IReadOnlyList<DhcpReservationEntity> reservations = await reservationRepository.ListAsync(
            null,
            stoppingToken
        );
        IReadOnlyList<DnsClientAddressEntity> clientAddresses =
            await clientAddressRepository.ListAsync(null, stoppingToken);
        IReadOnlyList<DnsClientStaticAddressMappingEntity> mappings =
            await staticMappingRepository.ListAsync(null, stoppingToken);

        int changed =
            NormalizeReservations(reservationRepository, reservations, logger)
            + NormalizeClientAddresses(clientAddressRepository, clientAddresses, logger)
            + NormalizeMappings(staticMappingRepository, mappings, logger);

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(stoppingToken);

        return changed;
    }

    /// <summary>
    ///     This method normalises reservation identifiers and addresses. The unique indexes are
    ///     <c>(scope, identifier)</c> and <c>(scope, address)</c>, so a row is blocked when another
    ///     row of its scope would end up with the same identifier or the same address.
    /// </summary>
    private static int NormalizeReservations(
        IDhcpReservationRepository repository,
        IReadOnlyList<DhcpReservationEntity> reservations,
        ILogger logger
    )
    {
        List<(DhcpReservationEntity Row, string Identifier, string Address)> targets = [];
        foreach (DhcpReservationEntity row in reservations)
        {
            if (!ClientIdentifierFormat.TryNormalize(row.ClientIdentifier, out string identifier))
            {
                logger.LogWarning(
                    "DHCP reservation '{ReservationId}' has the client identifier '{ClientIdentifier}', "
                        + "which is not a valid identifier, so it was left as it is and never matches "
                        + "a client. Fix or delete it.",
                    row.Id,
                    row.ClientIdentifier
                );
                continue;
            }

            string address =
                row.IpAddress != null
                && IPAddress.TryParse(row.IpAddress.Trim(), out IPAddress parsed)
                    ? parsed.ToString()
                    : row.IpAddress;
            targets.Add((row, identifier, address));
        }

        HashSet<DhcpReservationEntity> blocked = [];
        foreach (
            IGrouping<
                (Guid, string),
                (DhcpReservationEntity Row, string Identifier, string Address)
            > group in targets.GroupBy(target => (target.Row.ScopeId, target.Identifier))
        )
            Block(
                group.Select(t => t.Row).ToList(),
                "identifier",
                group.Key.Item2,
                blocked,
                logger
            );
        foreach (
            IGrouping<
                (Guid, string),
                (DhcpReservationEntity Row, string Identifier, string Address)
            > group in targets.GroupBy(target => (target.Row.ScopeId, target.Address))
        )
            Block(group.Select(t => t.Row).ToList(), "address", group.Key.Item2, blocked, logger);

        int changed = 0;
        foreach ((DhcpReservationEntity row, string identifier, string address) in targets)
        {
            if (blocked.Contains(row))
                continue;

            if (
                string.Equals(row.ClientIdentifier, identifier, StringComparison.Ordinal)
                && string.Equals(row.IpAddress, address, StringComparison.Ordinal)
            )
                continue;

            row.ClientIdentifier = identifier;
            row.IpAddress = address;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            repository.Update(row);
            changed++;
        }

        return changed;
    }

    /// <summary>
    ///     This method blocks a group of reservations that would share one identifier or address,
    ///     and warns once when at least one member is not already in that form.
    /// </summary>
    private static void Block(
        List<DhcpReservationEntity> members,
        string what,
        string normalForm,
        HashSet<DhcpReservationEntity> blocked,
        ILogger logger
    )
    {
        if (members.Count < 2)
            return;

        blocked.UnionWith(members);
        logger.LogWarning(
            "DHCP reservations {Reservations} would all get the same {What} '{NormalForm}' once "
                + "normalised, so none was changed. Only a reservation in the normal form matches "
                + "a client; delete or fix the others.",
            string.Join(", ", members.Select(row => $"'{row.ClientIdentifier}' ({row.IpAddress})")),
            what,
            normalForm
        );
    }

    /// <summary>
    ///     This method normalises client address identifiers. The unique index is
    ///     <c>(identifier, protocol version)</c>.
    /// </summary>
    private static int NormalizeClientAddresses(
        IDnsClientAddressRepository repository,
        IReadOnlyList<DnsClientAddressEntity> rows,
        ILogger logger
    )
    {
        List<(DnsClientAddressEntity Row, string Identifier)> targets = [];
        foreach (DnsClientAddressEntity row in rows)
        {
            if (!ClientIdentifierFormat.TryNormalize(row.ClientIdentifier, out string identifier))
            {
                logger.LogWarning(
                    "Client address '{AddressId}' has the client identifier '{ClientIdentifier}', "
                        + "which is not a valid identifier, so it was left as it is. Fix or delete it.",
                    row.Id,
                    row.ClientIdentifier
                );
                continue;
            }

            targets.Add((row, identifier));
        }

        int changed = 0;
        foreach (
            var group in targets.GroupBy(target => (target.Identifier, target.Row.ProtocolVersion))
        )
        {
            List<DnsClientAddressEntity> members = [.. group.Select(t => t.Row)];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Client addresses {Identifiers} would all get the identifier '{NormalForm}' "
                        + "(protocol {Protocol}) once normalised, so none was changed. Delete or fix "
                        + "the others.",
                    string.Join(", ", members.Select(row => $"'{row.ClientIdentifier}'")),
                    group.Key.Identifier,
                    group.Key.ProtocolVersion
                );
                continue;
            }

            DnsClientAddressEntity single = members[0];
            if (
                string.Equals(
                    single.ClientIdentifier,
                    group.Key.Identifier,
                    StringComparison.Ordinal
                )
            )
                continue;

            single.ClientIdentifier = group.Key.Identifier;
            single.UpdatedAtUtc = DateTimeOffset.UtcNow;
            repository.Update(single);
            changed++;
        }

        return changed;
    }

    /// <summary>
    ///     This method normalises static mapping identifiers. The table has no unique index on the
    ///     identifier, so every parseable row is normalised.
    /// </summary>
    private static int NormalizeMappings(
        IDnsClientStaticAddressMappingRepository repository,
        IReadOnlyList<DnsClientStaticAddressMappingEntity> rows,
        ILogger logger
    )
    {
        int changed = 0;
        foreach (DnsClientStaticAddressMappingEntity row in rows)
        {
            if (!ClientIdentifierFormat.TryNormalize(row.ClientIdentifier, out string identifier))
            {
                logger.LogWarning(
                    "Client static address mapping '{MappingId}' has the client identifier "
                        + "'{ClientIdentifier}', which is not a valid identifier, so it was left as "
                        + "it is. Fix or delete it.",
                    row.Id,
                    row.ClientIdentifier
                );
                continue;
            }

            if (string.Equals(row.ClientIdentifier, identifier, StringComparison.Ordinal))
                continue;

            row.ClientIdentifier = identifier;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            repository.Update(row);
            changed++;
        }

        return changed;
    }
}

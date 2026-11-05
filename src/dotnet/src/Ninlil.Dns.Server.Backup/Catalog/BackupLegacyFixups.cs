using System.Text.Json;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Catalog;

/// <summary>
///     This class holds the repairs a restore applies to rows read from an archive written by an
///     older version, before the rows reach the database. They are pure functions on the parsed
///     rows, so they can be tested without a database and run before anything is wiped.
/// </summary>
public static class BackupLegacyFixups
{
    /// <summary>
    ///     This method links cluster TSIG keys to their peers for an archive taken before
    ///     <see cref="TsigKeyEntity.ClusterNodeId" /> existed. Such an archive has no
    ///     <c>ClusterNodeId</c> property on its TsigKeys elements at all, while an archive written
    ///     since always carries the property (as null for an unlinked key), so a missing property
    ///     is the exact test and an archive that already has the link is never changed. A key is
    ///     linked to the first node whose <see cref="ClusterNodeEntity.TsigKeyName" /> equals the
    ///     key's name, compared ordinally; a key no node uses (an unused invite) stays unlinked.
    ///     This is the same rule as the MongoDB backfill and the relational migration.
    /// </summary>
    /// <param name="tsigKeysJson">The archive's <c>database/TsigKeys.json</c> text.</param>
    /// <param name="keys">The keys parsed from that text, in the same order.</param>
    /// <param name="nodes">The cluster nodes parsed from the archive.</param>
    /// <returns>The number of keys that were linked.</returns>
    public static int LinkClusterKeys(
        string tsigKeysJson,
        IList<TsigKeyEntity> keys,
        IReadOnlyList<ClusterNodeEntity> nodes
    )
    {
        if (string.IsNullOrWhiteSpace(tsigKeysJson) || keys.Count == 0 || nodes.Count == 0)
            return 0;

        using JsonDocument document = JsonDocument.Parse(
            tsigKeysJson,
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }
        );

        // The parsed list follows the array element for element; if it does not, nothing can be
        // matched safely.
        if (
            document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() != keys.Count
        )
            return 0;

        int linked = 0;
        int index = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            TsigKeyEntity key = keys[index++];
            if (
                element.ValueKind != JsonValueKind.Object
                || key is null
                || HasProperty(element, nameof(TsigKeyEntity.ClusterNodeId))
            )
                continue;

            ClusterNodeEntity node = nodes.FirstOrDefault(candidate =>
                string.Equals(candidate.TsigKeyName, key.Name, StringComparison.Ordinal)
            );
            if (node is null)
                continue;

            key.ClusterNodeId = node.Id;
            linked++;
        }

        return linked;
    }

    /// <summary>
    ///     This method links DHCP leases to their scopes for an archive taken before
    ///     <see cref="LeaseEntity.ScopeId" /> existed. Such an archive has no <c>ScopeId</c>
    ///     property on its DhcpLeases elements at all, while an archive written since always
    ///     carries it, so a missing property is the exact test and a lease that already has the
    ///     property is never changed. A lease takes the id of the archive's scope whose
    ///     <see cref="DhcpScopeEntity.Name" /> equals the lease's <see cref="LeaseEntity.ScopeName" />,
    ///     compared ordinally. A lease that matches no scope belonged to a deleted scope and is
    ///     removed from <paramref name="leases" />, because it would otherwise keep the empty id
    ///     and collide with other such leases on the unique indexes. This is the same rule as the
    ///     MongoDB backfill and the relational migration.
    /// </summary>
    /// <param name="leasesJson">The archive's <c>database/DhcpLeases.json</c> text.</param>
    /// <param name="leases">
    ///     The leases parsed from that text, in the same order. Orphaned leases are removed from it.
    /// </param>
    /// <param name="scopes">The DHCP scopes parsed from the archive.</param>
    /// <returns>The number of leases that were linked and the number that were removed.</returns>
    public static (int Linked, int Removed) LinkLeasesToScopes(
        string leasesJson,
        IList<LeaseEntity> leases,
        IReadOnlyList<DhcpScopeEntity> scopes
    )
    {
        if (string.IsNullOrWhiteSpace(leasesJson) || leases.Count == 0)
            return (0, 0);

        using JsonDocument document = JsonDocument.Parse(
            leasesJson,
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }
        );

        // The parsed list follows the array element for element; if it does not, nothing can be
        // matched safely.
        if (
            document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() != leases.Count
        )
            return (0, 0);

        List<LeaseEntity> orphans = [];
        int linked = 0;
        int index = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            LeaseEntity lease = leases[index++];
            if (
                element.ValueKind != JsonValueKind.Object
                || lease is null
                || HasProperty(element, nameof(LeaseEntity.ScopeId))
            )
                continue;

            DhcpScopeEntity scope = scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, lease.ScopeName, StringComparison.Ordinal)
            );
            if (scope is null)
            {
                orphans.Add(lease);
                continue;
            }

            lease.ScopeId = scope.Id;
            linked++;
        }

        foreach (LeaseEntity orphan in orphans)
            leases.Remove(orphan);

        return (linked, orphans.Count);
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element
            .EnumerateObject()
            .Any(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
            );
}

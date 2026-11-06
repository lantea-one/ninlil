using System.Reflection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Backup.Catalog;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class tests <see cref="BackupEntityCatalog" /> for completeness. It reflects over the
///     DNS and DHCP entity assemblies, so a new entity type must be put in
///     <see cref="BackupEntityCatalog.Entries" /> or <see cref="BackupEntityCatalog.ExcludedEntityTypes" />
///     before this test passes. It starts no host and needs no database.
/// </summary>
public sealed class BackupEntityCatalogTests
{
    /// <summary>
    ///     Every concrete entity type is either backed up or left out on purpose, never both.
    /// </summary>
    [Fact]
    public void EveryEntityType_IsEitherBackedUpOrExcludedOnPurpose()
    {
        HashSet<Type> covered = [.. BackupEntityCatalog.Entries.Select(entry => entry.EntityType)];
        HashSet<Type> excluded = [.. BackupEntityCatalog.ExcludedEntityTypes];

        // SystemBackupService writes the claims inside its identity bundle (user and role claim
        // assignments), not as catalogue entries, so they are covered by another route.
        HashSet<Type> bundled = [typeof(UserClaimEntity), typeof(RoleClaimEntity)];

        List<Type> unclassified =
        [
            .. new[] { typeof(EntityBase).Assembly, typeof(LeaseEntity).Assembly }
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type =>
                    type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(EntityBase))
                )
                .Where(type =>
                    !covered.Contains(type) && !excluded.Contains(type) && !bundled.Contains(type)
                ),
        ];

        Assert.True(
            unclassified.Count == 0,
            "Entity types in neither the backup catalogue nor its exclusions: "
                + string.Join(", ", unclassified.Select(type => type.Name))
        );
        Assert.Empty(covered.Intersect(excluded));
    }
}

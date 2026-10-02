using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ClusterSyncTombstoneEntity" /> onto the
///     <c>NinlilDnsClusterSyncTombstones</c> table.
/// </summary>
public class ClusterSyncTombstoneConfiguration
    : IEntityTypeConfiguration<ClusterSyncTombstoneEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ClusterSyncTombstoneEntity> builder)
    {
        builder.ToTable("NinlilDnsClusterSyncTombstones");

        builder.HasKey(tombstone => tombstone.Id);

        // At most one tombstone per entity; a later deletion updates it.
        builder.Property(tombstone => tombstone.EntityType).HasMaxLength(64).IsRequired();
        builder.Property(tombstone => tombstone.EntityKey).HasMaxLength(255).IsRequired();
        builder.HasIndex(tombstone => new { tombstone.EntityType, tombstone.EntityKey }).IsUnique();
    }
}

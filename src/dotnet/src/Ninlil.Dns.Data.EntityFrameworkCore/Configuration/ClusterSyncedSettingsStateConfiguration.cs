using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ClusterSyncedSettingsStateEntity" /> onto the
///     <c>NinlilDnsClusterSyncedSettingsStates</c> table.
/// </summary>
public class ClusterSyncedSettingsStateConfiguration
    : IEntityTypeConfiguration<ClusterSyncedSettingsStateEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ClusterSyncedSettingsStateEntity> builder)
    {
        // Map this entity onto the NinlilDnsClusterSyncedSettingsStates table.
        builder.ToTable("NinlilDnsClusterSyncedSettingsStates");

        // The Id property is the primary key.
        builder.HasKey(state => state.Id);

        // Exactly one recorded state per settings section.
        builder.Property(state => state.SectionName).HasMaxLength(255).IsRequired();
        builder.HasIndex(state => state.SectionName).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="SecondaryZoneRefreshStateEntity" /> onto the
///     <c>NinlilDnsSecondaryZoneRefreshStates</c> table.
/// </summary>
public class SecondaryZoneRefreshStateConfiguration
    : IEntityTypeConfiguration<SecondaryZoneRefreshStateEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<SecondaryZoneRefreshStateEntity> builder)
    {
        builder.ToTable("NinlilDnsSecondaryZoneRefreshStates");

        builder.HasKey(state => state.Id);

        // One row per zone, found by zone. There is no foreign key, like the change journal: the
        // row is node-local state that the refresh service cleans up itself.
        builder.HasIndex(state => state.ZoneId).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ZoneEntity" /> onto the <c>NinlilDnsZones</c> table.
/// </summary>
public class ZoneConfiguration : IEntityTypeConfiguration<ZoneEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ZoneEntity> builder)
    {
        // Map this entity onto the NinlilDnsZones table.
        builder.ToTable("NinlilDnsZones");

        // The Id property is the primary key.
        builder.HasKey(zone => zone.Id);

        // A zone's name is required, bounded to the DNS protocol's own 255-octet limit, and
        // unique -- this server cannot be authoritative for the same zone name twice.
        builder.Property(zone => zone.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(zone => zone.Name).IsUnique();

        // Store the zone type using its short, human-readable name rather than its numeric value.
        builder.Property(zone => zone.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(zone => zone.DnssecAlgorithm).HasConversion<string>().HasMaxLength(32);

        builder.Property(zone => zone.Comments).HasMaxLength(4096);
        builder.Property(zone => zone.TsigKeyName).HasMaxLength(255);
        builder.Property(zone => zone.CloudProviderZoneId).HasMaxLength(255);
        builder.Property(zone => zone.CaaIssuerDomain).HasMaxLength(255);

        // Explicit here, rather than left to only the C# property initializer, so a pre-existing
        // zone row being backfilled by this column's own migration gets the same 5-minute
        // default a brand-new zone gets, instead of TimeSpan.Zero.
        builder
            .Property(zone => zone.CloudSyncInterval)
            .HasDefaultValue(TimeSpan.FromMinutes(5));

        // Store the three address lists as JSON, since none of the four relational providers
        // Ninlil supports offer a portable native array column type.
        builder
            .Property(zone => zone.PrimaryServerAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(zone => zone.ForwarderAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(zone => zone.NotifyAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());
    }
}

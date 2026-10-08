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
        builder.ToTable("NinlilDnsZones");

        builder.HasKey(zone => zone.Id);

        // Zone names are capped at the 255-octet DNS limit and unique, since a server cannot hold
        // the same zone twice.
        builder.Property(zone => zone.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(zone => zone.Name).IsUnique();

        builder.Property(zone => zone.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(zone => zone.DnssecAlgorithm).HasConversion<string>().HasMaxLength(32);

        builder.Property(zone => zone.Comments).HasMaxLength(4096);
        builder.Property(zone => zone.TsigKeyName).HasMaxLength(255);
        builder.Property(zone => zone.CloudProviderZoneId).HasMaxLength(255);
        builder.Property(zone => zone.CaaIssuerDomain).HasMaxLength(255);

        // The default is set in the database as well as by the property initializer, so rows
        // that existed before this column was added get 5 minutes rather than
        // TimeSpan.Zero.
        builder
            .Property(zone => zone.CloudSyncInterval)
            .HasDefaultValue(TimeSpan.FromMinutes(5));

        builder
            .Property(zone => zone.PrimaryServerAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(zone => zone.ForwarderAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(zone => zone.NotifyAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(zone => zone.UpdateTsigKeyNames)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(zone => zone.TransferTsigKeyNames)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());
    }
}

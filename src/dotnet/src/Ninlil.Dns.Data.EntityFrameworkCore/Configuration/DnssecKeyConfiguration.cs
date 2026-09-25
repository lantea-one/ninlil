using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnssecKeyEntity" /> onto the <c>NinlilDnsDnssecKeys</c> table.
/// </summary>
public class DnssecKeyConfiguration : IEntityTypeConfiguration<DnssecKeyEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnssecKeyEntity> builder)
    {
        builder.ToTable("NinlilDnsDnssecKeys");

        builder.HasKey(key => key.Id);

        // The signing pipeline always loads keys by zone.
        builder.HasIndex(key => key.ZoneId);

        // Stored as UTC ticks so SearchByZoneIdAsync can order by it on SQLite; see
        // NinlilDnsContext.
        builder
            .Property(key => key.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );

        builder.Property(key => key.PublicKeyWireFormat).IsRequired();
        builder.Property(key => key.PrivateKeyPkcs8Encrypted).IsRequired();
        builder.Property(key => key.Comments).HasMaxLength(4096);
    }
}

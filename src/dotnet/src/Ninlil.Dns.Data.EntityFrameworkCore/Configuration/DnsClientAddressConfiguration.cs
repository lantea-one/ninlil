using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientAddressEntity" /> onto the
///     <c>NinlilDnsClientAddresses</c> table.
/// </summary>
public class DnsClientAddressConfiguration : IEntityTypeConfiguration<DnsClientAddressEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientAddressEntity> builder)
    {
        builder.ToTable("NinlilDnsClientAddresses");

        builder.HasKey(address => address.Id);

        builder.Property(address => address.ClientIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(address => address.Address).HasMaxLength(64);

        builder
            .Property(address => address.ProtocolVersion)
            .HasConversion<string>()
            .HasMaxLength(16);

        // An identifier belongs to one device only.
        builder
            .HasIndex(address => new { address.ClientIdentifier, address.ProtocolVersion })
            .IsUnique();

        // A device's addresses are often loaded by device.
        builder.HasIndex(address => address.ClientId);

        // The time columns are stored as UTC ticks so they can be ordered on SQLite (see
        // NinlilDnsContext); SearchByClientIdAsync orders by creation time.
        builder
            .Property(address => address.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );

        builder
            .Property(address => address.FirstSeenUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );

        builder
            .Property(address => address.LastSeenUtc)
            .HasConversion(
                value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null
            );
    }
}

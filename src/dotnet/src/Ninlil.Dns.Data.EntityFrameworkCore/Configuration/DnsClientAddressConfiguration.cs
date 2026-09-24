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
        // Map this entity onto the NinlilDnsClientAddresses table.
        builder.ToTable("NinlilDnsClientAddresses");

        // The Id property is the primary key.
        builder.HasKey(address => address.Id);

        builder.Property(address => address.ClientIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(address => address.Address).HasMaxLength(64);

        // Store the protocol version using its short, human-readable name rather than its
        // underlying number, the same convention DnsClientConfiguration already established for
        // the identical enum before this property moved here.
        builder
            .Property(address => address.ProtocolVersion)
            .HasConversion<string>()
            .HasMaxLength(16);

        // A single identifier can only ever belong to one real device -- the same "one row per
        // identifier/protocol pair" uniqueness DnsClientConfiguration's own composite index used
        // to enforce directly on DnsClientEntity itself.
        builder
            .HasIndex(address => new { address.ClientIdentifier, address.ProtocolVersion })
            .IsUnique();

        // Every device's own addresses are looked up by their owning client often enough (loading
        // a device's full address set, or resolving a device row by way of one of its addresses)
        // to warrant its own index.
        builder.HasIndex(address => address.ClientId);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly: SQLite's
        // own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset column at
        // all, which SearchByClientIdAsync's own creation-time paging needs -- the same conversion
        // DnsClientScheduleConfiguration.CreatedAtUtc already establishes for the identical shape
        // of problem.
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

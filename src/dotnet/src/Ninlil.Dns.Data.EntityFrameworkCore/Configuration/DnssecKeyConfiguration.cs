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
        // Map this entity onto the NinlilDnsDnssecKeys table.
        builder.ToTable("NinlilDnsDnssecKeys");

        // The Id property is the primary key.
        builder.HasKey(key => key.Id);

        // Every key belongs to exactly one zone, and a zone's signing pipeline always looks
        // its keys up by that zone -- an index makes that the fast path it needs to be.
        builder.HasIndex(key => key.ZoneId);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly: SQLite's
        // own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset column at
        // all, which SearchByZoneIdAsync's own role-then-creation-time paging needs. Every value
        // this column ever holds is already UTC, so converting to a plain UTC tick count loses
        // nothing and orders correctly and efficiently on every one of this project's 4
        // relational providers -- the same conversion
        // DnsQueryLogEntryConfiguration.OccurredAtUtc already establishes for the identical shape
        // of problem.
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

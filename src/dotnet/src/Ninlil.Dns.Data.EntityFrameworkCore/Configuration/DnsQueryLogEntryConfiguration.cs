using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsQueryLogEntryEntity" /> onto the
///     <c>NinlilDnsQueryLogEntries</c> table, with the indexes its own real search/retention
///     queries actually rely on -- unlike this project's own much lower-cardinality entities, a
///     query log genuinely needs pushed-down, indexed lookups rather than an in-memory filter over
///     the full table.
/// </summary>
public class DnsQueryLogEntryConfiguration : IEntityTypeConfiguration<DnsQueryLogEntryEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsQueryLogEntryEntity> builder)
    {
        builder.ToTable("NinlilDnsQueryLogEntries");

        builder.HasKey(entry => entry.Id);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly:
        // SQLite's own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset
        // column at all (its default text representation cannot be compared correctly once two
        // rows carry different offsets), which both the newest-first search ordering and the
        // retention sweep's own age comparison need. Every value this column ever holds is
        // already UTC (DnsQueryLogSubscriber never stores anything else), so converting to a
        // plain UTC tick count loses nothing and orders/compares correctly and efficiently on
        // every one of this project's 4 relational providers, not just SQLite.
        builder
            .Property(entry => entry.OccurredAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );

        // Every search orders by this column descending and every retention sweep filters on it,
        // so it is this table's single most important index.
        builder.HasIndex(entry => entry.OccurredAtUtc);

        builder.Property(entry => entry.ClientAddress).HasMaxLength(45);
        builder.HasIndex(entry => entry.ClientAddress);

        builder.Property(entry => entry.QueriedName).HasMaxLength(255);
        builder.HasIndex(entry => entry.QueriedName);

        builder.Property(entry => entry.Protocol).HasConversion<string>().HasMaxLength(32);

        builder.Property(entry => entry.QueriedType).HasConversion<string>().HasMaxLength(32);

        builder.Property(entry => entry.ResponseCode).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(entry => entry.ResponseCode);

        builder.Property(entry => entry.ClientIdentifier).HasMaxLength(256);

        // A per-client activity view searches by this column directly (ClientActivityController),
        // so it needs its own index exactly like ClientAddress above -- the same reasoning, since
        // this table can genuinely reach millions of rows.
        builder.HasIndex(entry => entry.ClientIdentifier);

        // Stored using its short, human-readable name, the same convention DnsClientConfiguration
        // already establishes for the identical enum.
        builder
            .Property(entry => entry.ClientProtocolVersion)
            .HasConversion<string>()
            .HasMaxLength(16);
    }
}

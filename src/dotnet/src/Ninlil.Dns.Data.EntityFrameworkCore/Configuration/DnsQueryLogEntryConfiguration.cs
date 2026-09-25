using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsQueryLogEntryEntity" /> onto the
///     <c>NinlilDnsQueryLogEntries</c> table, with the indexes its search and retention queries
///     rely on. The log can reach millions of rows, so every filter must be served by an index.
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

        // Stored as UTC ticks so the newest-first search and the retention sweep can order and
        // compare it on SQLite; see NinlilDnsContext.
        builder
            .Property(entry => entry.OccurredAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );

        // Every search orders by this column and every retention sweep filters on it, so it is the
        // most important index.
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

        // The per-client activity view (ClientActivityController) searches by this column.
        builder.HasIndex(entry => entry.ClientIdentifier);

        builder
            .Property(entry => entry.ClientProtocolVersion)
            .HasConversion<string>()
            .HasMaxLength(16);
    }
}

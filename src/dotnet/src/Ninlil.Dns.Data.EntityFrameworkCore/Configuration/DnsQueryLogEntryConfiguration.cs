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

        // Every unfiltered search orders by this column and every retention sweep filters and
        // orders on it, so it is the most important index. The composite indexes below start with
        // a filter column and end with this one, so a filtered newest-first page and its count
        // are an index range walk instead of a sort over every match.
        builder.HasIndex(entry => entry.OccurredAtUtc);

        builder.Property(entry => entry.ClientAddress).HasMaxLength(45);

        // Serves the client-address filter with the newest-first order and its count.
        builder.HasIndex(entry => new { entry.ClientAddress, entry.OccurredAtUtc });

        // No index on this column on purpose: the only name filter is an unanchored,
        // case-insensitive substring match (LIKE '%term%'), which no B-tree can seek.
        builder.Property(entry => entry.QueriedName).HasMaxLength(255);

        builder.Property(entry => entry.Protocol).HasConversion<string>().HasMaxLength(32);

        builder.Property(entry => entry.QueriedType).HasConversion<string>().HasMaxLength(32);

        builder.Property(entry => entry.ResponseCode).HasConversion<string>().HasMaxLength(32);

        // Serves the response-code filter with the newest-first order and its count.
        builder.HasIndex(entry => new { entry.ResponseCode, entry.OccurredAtUtc });

        builder.Property(entry => entry.ClientIdentifier).HasMaxLength(256);

        // The per-client activity view (ClientActivityController) searches by this column, newest
        // first.
        builder.HasIndex(entry => new { entry.ClientIdentifier, entry.OccurredAtUtc });

        builder
            .Property(entry => entry.ClientProtocolVersion)
            .HasConversion<string>()
            .HasMaxLength(16);
    }
}

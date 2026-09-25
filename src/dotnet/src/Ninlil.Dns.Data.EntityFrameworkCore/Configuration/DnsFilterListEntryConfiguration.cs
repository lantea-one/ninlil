using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsFilterListEntryEntity" /> onto the
///     <c>NinlilDnsFilterListEntries</c> table.
/// </summary>
public class DnsFilterListEntryConfiguration : IEntityTypeConfiguration<DnsFilterListEntryEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsFilterListEntryEntity> builder)
    {
        builder.ToTable("NinlilDnsFilterListEntries");

        builder.HasKey(entry => entry.Id);

        // The matching engine and the refresh work on one list's entries at a time. An index on
        // (ListId, Value) serves SearchByListIdAsync (WHERE ListId = ... ORDER BY Value) for
        // subscribed lists with tens of thousands of entries, and its ListId prefix serves the
        // plain lookups.
        builder.HasIndex(entry => new { entry.ListId, entry.Value });

        builder.Property(entry => entry.Value).HasMaxLength(512).IsRequired();

        builder.Property(entry => entry.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.Comment).HasMaxLength(1024);
    }
}

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
        // Map this entity onto the NinlilDnsFilterListEntries table.
        builder.ToTable("NinlilDnsFilterListEntries");

        // The Id property is the primary key.
        builder.HasKey(entry => entry.Id);

        // Every entry belongs to exactly one list, and both the matching engine and the
        // subscribed-list refresh service always work with a single list's entries at a time. A
        // composite index on (ListId, Value), rather than ListId alone, is what makes
        // SearchByListIdAsync's own WHERE ListId = ... ORDER BY Value query genuinely served by
        // an index -- not merely syntactically pushed down -- at the scale a subscribed list's
        // own entry set can reach (a real external block list can carry tens of thousands of
        // entries), while still covering every plain ListId-only lookup as a leftmost prefix.
        builder.HasIndex(entry => new { entry.ListId, entry.Value });

        builder.Property(entry => entry.Value).HasMaxLength(512).IsRequired();

        // Store the entry kind using its short, human-readable name rather than its numeric
        // value.
        builder.Property(entry => entry.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.Comment).HasMaxLength(1024);
    }
}

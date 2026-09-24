using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ZoneChangeJournalEntryEntity" /> onto the
///     <c>NinlilDnsZoneChangeJournalEntries</c> table.
/// </summary>
public class ZoneChangeJournalEntryConfiguration
    : IEntityTypeConfiguration<ZoneChangeJournalEntryEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ZoneChangeJournalEntryEntity> builder)
    {
        // Map this entity onto the NinlilDnsZoneChangeJournalEntries table.
        builder.ToTable("NinlilDnsZoneChangeJournalEntries");

        // The Id property is the primary key.
        builder.HasKey(entry => entry.Id);

        // Every lookup an IXFR responder makes is "every entry for this zone, from this serial
        // onward", so both columns together are what actually needs to be indexed.
        builder.HasIndex(entry => new { entry.ZoneId, entry.OldSerial });

        builder.Property(entry => entry.RemovedRecordsJson).IsRequired();
        builder.Property(entry => entry.AddedRecordsJson).IsRequired();
    }
}

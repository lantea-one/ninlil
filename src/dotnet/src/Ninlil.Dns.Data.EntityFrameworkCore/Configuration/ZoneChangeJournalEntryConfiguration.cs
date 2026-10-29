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
        builder.ToTable("NinlilDnsZoneChangeJournalEntries");

        builder.HasKey(entry => entry.Id);

        // The IXFR responder asks for every entry of a zone from a serial onward.
        builder.HasIndex(entry => new { entry.ZoneId, entry.OldSerial });

        builder.Property(entry => entry.RemovedRecordsJson).IsRequired();
        builder.Property(entry => entry.AddedRecordsJson).IsRequired();

        // Null is valid: entries written before SOA history existed have none, and the first
        // entry of a zone has no older SOA.
        builder.Property(entry => entry.OldSoaRecordJson).IsRequired(false);
        builder.Property(entry => entry.NewSoaRecordJson).IsRequired(false);
    }
}

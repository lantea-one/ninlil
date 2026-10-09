using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="RecordEntity" /> onto the <c>NinlilDnsRecords</c> table.
/// </summary>
public class RecordConfiguration : IEntityTypeConfiguration<RecordEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<RecordEntity> builder)
    {
        builder.ToTable("NinlilDnsRecords");

        builder.HasKey(record => record.Id);

        builder.Property(record => record.Name).HasMaxLength(255).IsRequired();

        builder.Property(record => record.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.Class).HasConversion<string>().HasMaxLength(16);

        // Not every provider has an unsigned 32-bit column, so the TTL is stored as a signed
        // 64-bit integer, which holds its whole range.
        builder.Property(record => record.TimeToLiveSeconds).HasConversion<long>();

        builder.Property(record => record.DataJson).IsRequired();
        builder.Property(record => record.Comments).HasMaxLength(4096);

        builder
            .Property(record => record.ManagedByFeature)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Answering a query looks up the records at a name, often of one type, within a zone.
        builder.HasIndex(record => new
        {
            record.ZoneId,
            record.Name,
            record.Type,
        });

        // PtrSyncService finds the records one feature manages, of one type, across all zones.
        builder.HasIndex(record => new { record.ManagedByFeature, record.Type });

        // Deleting a zone deletes its records.
        builder
            .HasOne<ZoneEntity>()
            .WithMany()
            .HasForeignKey(record => record.ZoneId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

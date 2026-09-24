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
        // Map this entity onto the NinlilDnsRecords table.
        builder.ToTable("NinlilDnsRecords");

        // The Id property is the primary key.
        builder.HasKey(record => record.Id);

        builder.Property(record => record.Name).HasMaxLength(255).IsRequired();

        // Store the type and class using their short, human-readable names rather than their
        // numeric values.
        builder.Property(record => record.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.Class).HasConversion<string>().HasMaxLength(16);

        // Microsoft SQL Server has no native unsigned integer column type, unlike SQLite,
        // PostgreSQL, and MySQL/MariaDB, all of which happily store a uint as-is. Storing it as a
        // 64-bit signed integer instead keeps a single, portable column type across every
        // provider without narrowing the 32-bit range a DNS TTL actually needs.
        builder.Property(record => record.TimeToLiveSeconds).HasConversion<long>();

        builder.Property(record => record.DataJson).IsRequired();
        builder.Property(record => record.Comments).HasMaxLength(4096);

        // Store the provenance marker using its short, human-readable name rather than its
        // numeric value, matching every other enum column on this entity.
        builder
            .Property(record => record.ManagedByFeature)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Answering a query means finding every record at a name (and often narrowing by type)
        // within a zone, so index exactly that access pattern.
        builder.HasIndex(record => new
        {
            record.ZoneId,
            record.Name,
            record.Type,
        });

        // Deleting a zone should clean up the records that belong to it.
        builder
            .HasOne<ZoneEntity>()
            .WithMany()
            .HasForeignKey(record => record.ZoneId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

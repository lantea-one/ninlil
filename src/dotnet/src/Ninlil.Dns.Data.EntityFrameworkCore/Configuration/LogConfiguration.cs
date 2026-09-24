using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="LogEntity" /> onto the <c>NinlilDnsLogs</c> table.
/// </summary>
public class LogConfiguration : IEntityTypeConfiguration<LogEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<LogEntity> builder)
    {
        // Map this entity onto the NinlilDnsLogs table.
        builder.ToTable("NinlilDnsLogs");

        // The Id property is the primary key.
        builder.HasKey(log => log.Id);

        // Store the severity using its short, human-readable name rather than its numeric value.
        builder.Property(log => log.Severity).HasConversion<string>().HasMaxLength(32);

        builder.Property(log => log.Category).HasMaxLength(512);
        builder.Property(log => log.Message).IsRequired();

        // The log viewer always searches by time first, then narrows by severity, so index both
        // together in the order they are actually filtered.
        builder.HasIndex(log => new { log.TimestampUtc, log.Severity });
    }
}

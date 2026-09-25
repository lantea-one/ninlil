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
        builder.ToTable("NinlilDnsLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Severity).HasConversion<string>().HasMaxLength(32);

        builder.Property(log => log.Category).HasMaxLength(512);
        builder.Property(log => log.Message).IsRequired();

        // The log viewer filters by time first, then severity.
        builder.HasIndex(log => new { log.TimestampUtc, log.Severity });
    }
}

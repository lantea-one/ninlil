using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientScheduleEntity" /> onto the
///     <c>NinlilDnsClientSchedules</c> table.
/// </summary>
public class DnsClientScheduleConfiguration : IEntityTypeConfiguration<DnsClientScheduleEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientScheduleEntity> builder)
    {
        builder.ToTable("NinlilDnsClientSchedules");

        builder.HasKey(schedule => schedule.Id);

        // The schedule engine looks schedules up by their owning client.
        builder.HasIndex(schedule => schedule.ClientId);

        builder.Property(schedule => schedule.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(schedule => schedule.Comments).HasMaxLength(4096);

        // Stored as UTC ticks so it can be ordered on SQLite; see NinlilDnsContext.
        builder
            .Property(schedule => schedule.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

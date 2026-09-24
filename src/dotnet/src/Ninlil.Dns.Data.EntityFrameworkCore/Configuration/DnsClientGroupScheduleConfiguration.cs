using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientGroupScheduleEntity" /> onto the
///     <c>NinlilDnsClientGroupSchedules</c> table.
/// </summary>
public class DnsClientGroupScheduleConfiguration
    : IEntityTypeConfiguration<DnsClientGroupScheduleEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientGroupScheduleEntity> builder)
    {
        // Map this entity onto the NinlilDnsClientGroupSchedules table.
        builder.ToTable("NinlilDnsClientGroupSchedules");

        // The Id property is the primary key.
        builder.HasKey(schedule => schedule.Id);

        // The schedule engine looks schedules up by their owning client group.
        builder.HasIndex(schedule => schedule.ClientGroupId);

        builder.Property(schedule => schedule.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(schedule => schedule.Comments).HasMaxLength(4096);

        // Stored as UTC ticks -- see DnsClientScheduleConfiguration.CreatedAtUtc's own doc
        // comment for the identical reasoning.
        builder
            .Property(schedule => schedule.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

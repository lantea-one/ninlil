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
        // Map this entity onto the NinlilDnsClientSchedules table.
        builder.ToTable("NinlilDnsClientSchedules");

        // The Id property is the primary key.
        builder.HasKey(schedule => schedule.Id);

        // The schedule engine looks schedules up by their owning client.
        builder.HasIndex(schedule => schedule.ClientId);

        builder.Property(schedule => schedule.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(schedule => schedule.Comments).HasMaxLength(4096);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly:
        // SQLite's own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset
        // column at all, which SearchByClientIdAsync's own creation-time paging needs -- the same
        // conversion DnsClientListAssignmentConfiguration.CreatedAtUtc already establishes for the
        // identical shape of problem.
        builder
            .Property(schedule => schedule.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

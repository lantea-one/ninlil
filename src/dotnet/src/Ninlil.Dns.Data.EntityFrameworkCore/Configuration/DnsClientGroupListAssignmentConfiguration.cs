using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientGroupListAssignmentEntity" /> onto the
///     <c>NinlilDnsClientGroupListAssignments</c> table.
/// </summary>
public class DnsClientGroupListAssignmentConfiguration
    : IEntityTypeConfiguration<DnsClientGroupListAssignmentEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientGroupListAssignmentEntity> builder)
    {
        builder.ToTable("NinlilDnsClientGroupListAssignments");

        builder.HasKey(assignment => assignment.Id);

        // Assignments are looked up by group and by list, so both keys are indexed.
        builder.HasIndex(assignment => assignment.ClientGroupId);
        builder.HasIndex(assignment => assignment.ListId);

        // Stored as UTC ticks so SearchByGroupIdAsync can order by it on SQLite; see
        // NinlilDnsContext.
        builder
            .Property(assignment => assignment.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

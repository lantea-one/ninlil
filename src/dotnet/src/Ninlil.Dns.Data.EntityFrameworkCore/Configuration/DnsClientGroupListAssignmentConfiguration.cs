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
        // Map this entity onto the NinlilDnsClientGroupListAssignments table.
        builder.ToTable("NinlilDnsClientGroupListAssignments");

        // The Id property is the primary key.
        builder.HasKey(assignment => assignment.Id);

        // The filtering engine looks assignments up from both directions -- every list assigned
        // to a group, and (indirectly, via the owning list) every group a list is assigned to --
        // so both foreign keys are indexed.
        builder.HasIndex(assignment => assignment.ClientGroupId);
        builder.HasIndex(assignment => assignment.ListId);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly:
        // SQLite's own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset
        // column at all, which SearchByGroupIdAsync's own creation-time paging needs. Every value
        // this column ever holds is already UTC, so converting to a plain UTC tick count loses
        // nothing and orders correctly and efficiently on every one of this project's 4
        // relational providers -- the same conversion DnssecKeyConfiguration.CreatedAtUtc/
        // DnsQueryLogEntryConfiguration.OccurredAtUtc already establish for the identical shape of
        // problem.
        builder
            .Property(assignment => assignment.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

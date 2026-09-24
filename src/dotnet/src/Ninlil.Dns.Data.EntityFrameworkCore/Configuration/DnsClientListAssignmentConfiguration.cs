using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientListAssignmentEntity" /> onto the
///     <c>NinlilDnsClientListAssignments</c> table.
/// </summary>
public class DnsClientListAssignmentConfiguration
    : IEntityTypeConfiguration<DnsClientListAssignmentEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientListAssignmentEntity> builder)
    {
        // Map this entity onto the NinlilDnsClientListAssignments table.
        builder.ToTable("NinlilDnsClientListAssignments");

        // The Id property is the primary key.
        builder.HasKey(assignment => assignment.Id);

        // The filtering engine looks assignments up from both directions -- every list assigned
        // to a client, and (indirectly, via the owning list) every client a list is assigned to --
        // so both foreign keys are indexed.
        builder.HasIndex(assignment => assignment.ClientId);
        builder.HasIndex(assignment => assignment.ListId);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly:
        // SQLite's own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset
        // column at all, which SearchByClientIdAsync's own creation-time paging needs. Every
        // value this column ever holds is already UTC, so converting to a plain UTC tick count
        // loses nothing and orders correctly and efficiently on every one of this project's 4
        // relational providers -- the same conversion DnsClientGroupListAssignmentConfiguration
        // .CreatedAtUtc already establishes for the identical shape of problem.
        builder
            .Property(assignment => assignment.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

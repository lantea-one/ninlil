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
        builder.ToTable("NinlilDnsClientListAssignments");

        builder.HasKey(assignment => assignment.Id);

        // Assignments are looked up by client and by list, so both keys are indexed.
        builder.HasIndex(assignment => assignment.ClientId);
        builder.HasIndex(assignment => assignment.ListId);

        // Stored as UTC ticks so it can be ordered on SQLite; see NinlilDnsContext.
        builder
            .Property(assignment => assignment.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

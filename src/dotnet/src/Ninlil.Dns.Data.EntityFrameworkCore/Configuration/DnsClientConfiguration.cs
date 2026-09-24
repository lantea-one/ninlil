using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientEntity" /> onto the <c>NinlilDnsClients</c> table.
/// </summary>
public class DnsClientConfiguration : IEntityTypeConfiguration<DnsClientEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientEntity> builder)
    {
        // Map this entity onto the NinlilDnsClients table.
        builder.ToTable("NinlilDnsClients");

        // The Id property is the primary key.
        builder.HasKey(client => client.Id);

        builder.Property(client => client.DisplayName).HasMaxLength(256);
        builder.Property(client => client.Comments).HasMaxLength(4096);

        // Store the origin using its short, human-readable name rather than its underlying
        // number, the same convention every other stored enum in this project establishes.
        builder.Property(client => client.Origin).HasConversion<string>().HasMaxLength(16);

        // The chosen device icon follows the same "stored by name" convention; null (no explicit
        // choice) stays null.
        builder.Property(client => client.DeviceIconKey).HasConversion<string>().HasMaxLength(32);

        // A client explicitly assigned to a group is looked up by that group often enough (a
        // group being deleted or edited) to warrant its own index.
        builder.HasIndex(client => client.ClientGroupId);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly: SQLite's
        // own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset column at
        // all, which SearchAsync's own creation-time paging needs now that a device no longer
        // carries a single identifier of its own to order by -- the same conversion
        // DnsClientScheduleConfiguration.CreatedAtUtc already establishes for the identical shape
        // of problem.
        builder
            .Property(client => client.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

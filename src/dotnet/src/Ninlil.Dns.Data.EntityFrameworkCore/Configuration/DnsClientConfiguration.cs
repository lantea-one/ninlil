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
        builder.ToTable("NinlilDnsClients");

        builder.HasKey(client => client.Id);

        builder.Property(client => client.DisplayName).HasMaxLength(256);
        builder.Property(client => client.Comments).HasMaxLength(4096);

        builder.Property(client => client.Origin).HasConversion<string>().HasMaxLength(16);

        builder.Property(client => client.DeviceIconKey).HasConversion<string>().HasMaxLength(32);

        // Clients assigned to a group are looked up when the group is edited or deleted.
        builder.HasIndex(client => client.ClientGroupId);

        // Stored as UTC ticks so SearchAsync can order by it on SQLite; see NinlilDnsContext.
        builder
            .Property(client => client.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

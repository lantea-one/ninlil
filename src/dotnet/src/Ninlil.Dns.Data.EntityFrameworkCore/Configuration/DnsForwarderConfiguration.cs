using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsForwarderEntity" /> onto the
///     <c>NinlilDnsForwarders</c> table.
/// </summary>
public class DnsForwarderConfiguration : IEntityTypeConfiguration<DnsForwarderEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsForwarderEntity> builder)
    {
        builder.ToTable("NinlilDnsForwarders");

        builder.HasKey(forwarder => forwarder.Id);

        // Forwarder names are unique.
        builder.Property(forwarder => forwarder.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(forwarder => forwarder.Name).IsUnique();

        builder.Property(forwarder => forwarder.DefinitionString).HasMaxLength(2048).IsRequired();
        builder.Property(forwarder => forwarder.Comments).HasMaxLength(4096);
    }
}

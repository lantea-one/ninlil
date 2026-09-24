using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientGroupNetworkEntity" /> onto the
///     <c>NinlilDnsClientGroupNetworks</c> table.
/// </summary>
public class DnsClientGroupNetworkConfiguration
    : IEntityTypeConfiguration<DnsClientGroupNetworkEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientGroupNetworkEntity> builder)
    {
        // Map this entity onto the NinlilDnsClientGroupNetworks table.
        builder.ToTable("NinlilDnsClientGroupNetworks");

        // The Id property is the primary key.
        builder.HasKey(network => network.Id);

        // Every network belongs to exactly one client group, and the filtering engine always
        // looks up a group's own networks together, so index the owning group.
        builder.HasIndex(network => network.ClientGroupId);

        // 64 characters comfortably fits the longest legal IPv6 CIDR notation.
        builder.Property(network => network.Cidr).HasMaxLength(64).IsRequired();
    }
}

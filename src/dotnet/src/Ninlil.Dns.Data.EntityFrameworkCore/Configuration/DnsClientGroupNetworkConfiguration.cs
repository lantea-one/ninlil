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
        builder.ToTable("NinlilDnsClientGroupNetworks");

        builder.HasKey(network => network.Id);

        // The filtering engine loads a group's rules together.
        builder.HasIndex(network => network.ClientGroupId);

        // 64 characters fits the longest IPv6 CIDR notation. The column is nullable because a row
        // that matches by DNS-over-HTTPS ClientID has no CIDR.
        builder.Property(network => network.Cidr).HasMaxLength(64);
    }
}

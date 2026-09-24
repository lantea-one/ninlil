using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientStaticAddressMappingEntity" /> onto the
///     <c>NinlilDnsClientStaticAddressMappings</c> table.
/// </summary>
public class DnsClientStaticAddressMappingConfiguration
    : IEntityTypeConfiguration<DnsClientStaticAddressMappingEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientStaticAddressMappingEntity> builder)
    {
        builder.ToTable("NinlilDnsClientStaticAddressMappings");

        builder.HasKey(mapping => mapping.Id);

        builder.Property(mapping => mapping.Cidr).HasMaxLength(64).IsRequired();
        builder.Property(mapping => mapping.ClientIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(mapping => mapping.Comments).HasMaxLength(4096);

        // Store the protocol version using its short, human-readable name rather than its
        // underlying number, the same convention DnsClientConfiguration.ProtocolVersion already
        // establishes for the identical enum.
        builder
            .Property(mapping => mapping.ProtocolVersion)
            .HasConversion<string>()
            .HasMaxLength(16);

        // The resolver's own hot path scans every enabled mapping on each resolution attempt --
        // this index lets that scan skip a disabled row without materializing it at all.
        builder.HasIndex(mapping => mapping.IsDisabled);
    }
}

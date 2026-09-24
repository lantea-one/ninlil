using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ClusterNodeEntity" /> onto the <c>NinlilDnsClusterNodes</c>
///     table.
/// </summary>
public class ClusterNodeConfiguration : IEntityTypeConfiguration<ClusterNodeEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ClusterNodeEntity> builder)
    {
        // Map this entity onto the NinlilDnsClusterNodes table.
        builder.ToTable("NinlilDnsClusterNodes");

        // The Id property is the primary key.
        builder.HasKey(node => node.Id);

        builder.Property(node => node.Name).HasMaxLength(255).IsRequired();

        // A given peer's own stable identity must appear at most once in this registry -- the
        // join handshake relies on this to detect a peer that is already known, possibly under a
        // different address.
        builder.HasIndex(node => node.RemoteNodeId).IsUnique();

        builder.Property(node => node.Host).HasMaxLength(255).IsRequired();

        builder.Property(node => node.TsigKeyName).HasMaxLength(255).IsRequired();

        builder.Property(node => node.TlsCertificateThumbprint).HasMaxLength(64).IsRequired();

        // Store the status using its short, human-readable name rather than its numeric value,
        // matching every other enum column this project stores.
        builder.Property(node => node.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(node => node.Comments).HasMaxLength(4096);
    }
}

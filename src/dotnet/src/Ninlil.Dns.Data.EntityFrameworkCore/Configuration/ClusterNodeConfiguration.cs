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
        builder.ToTable("NinlilDnsClusterNodes");

        builder.HasKey(node => node.Id);

        builder.Property(node => node.Name).HasMaxLength(255).IsRequired();

        // A peer's node identifier appears at most once; the join handshake relies on it to
        // detect a peer already known under another address.
        builder.HasIndex(node => node.RemoteNodeId).IsUnique();

        builder.Property(node => node.Host).HasMaxLength(255).IsRequired();

        builder.Property(node => node.TsigKeyName).HasMaxLength(255).IsRequired();

        builder.Property(node => node.TlsCertificateThumbprint).HasMaxLength(64).IsRequired();

        builder.Property(node => node.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(node => node.Comments).HasMaxLength(4096);
    }
}

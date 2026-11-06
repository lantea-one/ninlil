using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ClusterPeerConfigBackfillEntity" /> onto the
///     <c>NinlilDnsClusterPeerConfigBackfills</c> table.
/// </summary>
public class ClusterPeerConfigBackfillConfiguration
    : IEntityTypeConfiguration<ClusterPeerConfigBackfillEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ClusterPeerConfigBackfillEntity> builder)
    {
        builder.ToTable("NinlilDnsClusterPeerConfigBackfills");

        builder.HasKey(request => request.Id);

        // One request per peer: a second request updates the row instead of adding one.
        builder.HasIndex(request => request.ClusterNodeId).IsUnique();

        builder.Property(request => request.LastError).HasMaxLength(1000);

        // Removing a peer removes its request, so no orphan can start a pass for a peer that is gone.
        builder
            .HasOne<ClusterNodeEntity>()
            .WithMany()
            .HasForeignKey(request => request.ClusterNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="LeaseEntity" /> onto the <c>NinlilDhcpLeases</c> table.
/// </summary>
public class LeaseConfiguration : IEntityTypeConfiguration<LeaseEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<LeaseEntity> builder)
    {
        // Map this entity onto the NinlilDhcpLeases table.
        builder.ToTable("NinlilDhcpLeases");

        // The Id property is the primary key.
        builder.HasKey(lease => lease.Id);

        builder.Property(lease => lease.ScopeName).HasMaxLength(256).IsRequired();
        builder.Property(lease => lease.ClientIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(lease => lease.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(lease => lease.HostName).HasMaxLength(256);

        // Store the protocol version and state using their short, human-readable names rather
        // than their numeric values.
        builder.Property(lease => lease.ProtocolVersion).HasConversion<string>().HasMaxLength(16);
        builder.Property(lease => lease.State).HasConversion<string>().HasMaxLength(16);

        // A single client can only hold one lease in a given scope at a time, and a single IP
        // address can only be held by one lease in a given scope at a time.
        builder.HasIndex(lease => new { lease.ScopeName, lease.ClientIdentifier }).IsUnique();
        builder.HasIndex(lease => new { lease.ScopeName, lease.IpAddress }).IsUnique();

        // A plain, non-unique index on IpAddress alone (not scoped to ScopeName) backs
        // ILeaseRepository.FindActiveByIpAddressAsync's own cross-scope lookup -- the query a
        // client identity resolution needs, since a DNS query's own source address carries no
        // scope name at all.
        builder.HasIndex(lease => lease.IpAddress);
    }
}

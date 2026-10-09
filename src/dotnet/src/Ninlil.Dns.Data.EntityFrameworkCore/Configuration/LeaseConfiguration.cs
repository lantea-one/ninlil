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
        builder.ToTable("NinlilDhcpLeases");

        builder.HasKey(lease => lease.Id);

        builder.Property(lease => lease.ScopeName).HasMaxLength(256).IsRequired();
        builder.Property(lease => lease.ClientIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(lease => lease.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(lease => lease.HostName).HasMaxLength(256);

        builder.Property(lease => lease.ProtocolVersion).HasConversion<string>().HasMaxLength(16);
        builder.Property(lease => lease.State).HasConversion<string>().HasMaxLength(16);

        // Both lease instants are stored as UTC ticks so every engine can compare and order them
        // in SQL (the SQLite provider cannot translate DateTimeOffset comparisons).
        builder
            .Property(lease => lease.LeaseObtainedAtUtc)
            .HasConversion(
                value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null
            );

        builder
            .Property(lease => lease.LeaseExpiresAtUtc)
            .HasConversion(
                value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null
            );

        // Within a scope, a client holds at most one lease and an address is leased at most
        // once.
        builder.HasIndex(lease => new { lease.ScopeName, lease.ClientIdentifier }).IsUnique();
        builder.HasIndex(lease => new { lease.ScopeName, lease.IpAddress }).IsUnique();

        // A non-unique index on IpAddress alone backs the cross-scope lookup in
        // ILeaseRepository.FindActiveByIpAddressAsync, which client identity resolution uses
        // because a DNS query's source address says nothing about the scope.
        builder.HasIndex(lease => lease.IpAddress);
    }
}

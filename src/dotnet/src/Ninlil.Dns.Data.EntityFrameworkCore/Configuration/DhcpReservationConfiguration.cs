using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DhcpReservationEntity" /> onto the
///     <c>NinlilDhcpReservations</c> table.
/// </summary>
public class DhcpReservationConfiguration : IEntityTypeConfiguration<DhcpReservationEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DhcpReservationEntity> builder)
    {
        // Map this entity onto the NinlilDhcpReservations table.
        builder.ToTable("NinlilDhcpReservations");

        // The Id property is the primary key.
        builder.HasKey(reservation => reservation.Id);

        builder
            .Property(reservation => reservation.ClientIdentifier)
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(reservation => reservation.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(reservation => reservation.HostName).HasMaxLength(256);
        builder.Property(reservation => reservation.Comments).HasMaxLength(4096);

        // A single client can only hold one reservation in a given scope, and a single address
        // can only be reserved once in a given scope -- the same uniqueness shape LeaseConfiguration
        // already establishes for an active lease.
        builder
            .HasIndex(reservation => new { reservation.ScopeId, reservation.ClientIdentifier })
            .IsUnique();
        builder
            .HasIndex(reservation => new { reservation.ScopeId, reservation.IpAddress })
            .IsUnique();

        // Deleting a scope should clean up the reservations that belong to it.
        builder
            .HasOne<DhcpScopeEntity>()
            .WithMany()
            .HasForeignKey(reservation => reservation.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

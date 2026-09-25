using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="RoleClaimEntity" /> onto the <c>NinlilDnsRoleClaims</c>
///     table.
/// </summary>
public class RoleClaimConfiguration : IEntityTypeConfiguration<RoleClaimEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<RoleClaimEntity> builder)
    {
        builder.ToTable("NinlilDnsRoleClaims");

        builder.HasKey(claim => claim.Id);

        builder.Property(claim => claim.ClaimType).HasMaxLength(256).IsRequired();
        builder.Property(claim => claim.ClaimValue).HasMaxLength(1024).IsRequired();

        // A role's claims are loaded together.
        builder.HasIndex(claim => claim.RoleId);

        // Deleting a role deletes its claims.
        builder
            .HasOne<RoleEntity>()
            .WithMany()
            .HasForeignKey(claim => claim.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

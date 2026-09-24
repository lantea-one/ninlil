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
        // Map this entity onto the NinlilDnsRoleClaims table.
        builder.ToTable("NinlilDnsRoleClaims");

        // The Id property is the primary key.
        builder.HasKey(claim => claim.Id);

        // A claim's type and value are both required.
        builder.Property(claim => claim.ClaimType).HasMaxLength(256).IsRequired();
        builder.Property(claim => claim.ClaimValue).HasMaxLength(1024).IsRequired();

        // Every claim granted by a role is looked up together, so index the owning role.
        builder.HasIndex(claim => claim.RoleId);

        // Deleting a role should clean up the claims that reference it.
        builder
            .HasOne<RoleEntity>()
            .WithMany()
            .HasForeignKey(claim => claim.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

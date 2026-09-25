using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="UserClaimEntity" /> onto the <c>NinlilDnsUserClaims</c>
///     table.
/// </summary>
public class UserClaimConfiguration : IEntityTypeConfiguration<UserClaimEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<UserClaimEntity> builder)
    {
        builder.ToTable("NinlilDnsUserClaims");

        builder.HasKey(claim => claim.Id);

        builder.Property(claim => claim.ClaimType).HasMaxLength(256).IsRequired();
        builder.Property(claim => claim.ClaimValue).HasMaxLength(1024).IsRequired();

        // A user's claims are loaded together.
        builder.HasIndex(claim => claim.UserId);

        // Deleting a user deletes their claims.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(claim => claim.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

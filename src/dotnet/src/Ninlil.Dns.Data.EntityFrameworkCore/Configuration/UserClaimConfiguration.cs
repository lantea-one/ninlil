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
        // Map this entity onto the NinlilDnsUserClaims table.
        builder.ToTable("NinlilDnsUserClaims");

        // The Id property is the primary key.
        builder.HasKey(claim => claim.Id);

        // A claim's type and value are both required.
        builder.Property(claim => claim.ClaimType).HasMaxLength(256).IsRequired();
        builder.Property(claim => claim.ClaimValue).HasMaxLength(1024).IsRequired();

        // Every claim attached to a user is looked up together, so index the owning user.
        builder.HasIndex(claim => claim.UserId);

        // Deleting a user should clean up the claims that reference it.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(claim => claim.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

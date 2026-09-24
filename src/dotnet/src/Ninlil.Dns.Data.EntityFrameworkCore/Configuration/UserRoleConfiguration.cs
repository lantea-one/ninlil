using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="UserRoleEntity" /> onto the <c>NinlilDnsUserRoles</c> table,
///     the join table between users and roles.
/// </summary>
public class UserRoleConfiguration : IEntityTypeConfiguration<UserRoleEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<UserRoleEntity> builder)
    {
        // Map this entity onto the NinlilDnsUserRoles table.
        builder.ToTable("NinlilDnsUserRoles");

        // A user can only hold a given role once, so the pair forms the primary key.
        builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });

        // Deleting a user or a role should clean up the membership rows that reference it.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<RoleEntity>()
            .WithMany()
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

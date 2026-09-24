using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="RoleEntity" /> onto the <c>NinlilDnsRoles</c> table.
/// </summary>
public class RoleConfiguration : IEntityTypeConfiguration<RoleEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<RoleEntity> builder)
    {
        // Map this entity onto the NinlilDnsRoles table.
        builder.ToTable("NinlilDnsRoles");

        // The Id property is the primary key.
        builder.HasKey(role => role.Id);

        // A role's name is required, bounded in length, and unique.
        builder.Property(role => role.Name).HasMaxLength(256).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(256).IsRequired();
        builder.HasIndex(role => role.NormalizedName).IsUnique();

        // Null for every ordinary role -- see RoleEntity.ClusterConflictNote's own doc comment for
        // when the cluster's own identity/RBAC federation mechanism sets this.
        builder.Property(role => role.ClusterConflictNote).HasMaxLength(1024);
    }
}

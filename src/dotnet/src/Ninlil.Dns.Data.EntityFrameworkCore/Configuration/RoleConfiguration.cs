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
        builder.ToTable("NinlilDnsRoles");

        builder.HasKey(role => role.Id);

        // Role names are unique, compared through the normalized name.
        builder.Property(role => role.Name).HasMaxLength(256).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(256).IsRequired();
        builder.HasIndex(role => role.NormalizedName).IsUnique();

        // Null for an ordinary role; see RoleEntity.ClusterConflictNote.
        builder.Property(role => role.ClusterConflictNote).HasMaxLength(1024);
    }
}

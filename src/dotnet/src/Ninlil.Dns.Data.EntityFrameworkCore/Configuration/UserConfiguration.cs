using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="UserEntity" /> onto the <c>NinlilDnsUsers</c> table. Every
///     table in this solution follows the same naming convention: the <c>NinlilDns</c> prefix
///     identifies the product, and the rest of the name is the entity's plural, UpperCamelCase
///     name. Columns are left at Entity Framework Core's own default of matching the C# property
///     name exactly, which is already UpperCamelCase, so no further configuration is needed to
///     satisfy that convention.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        // Map this entity onto the NinlilDnsUsers table.
        builder.ToTable("NinlilDnsUsers");

        // The Id property is the primary key.
        builder.HasKey(user => user.Id);

        // A sign-in name and e-mail address are both required and bounded in length.
        builder.Property(user => user.UserName).HasMaxLength(256).IsRequired();
        builder.Property(user => user.NormalizedUserName).HasMaxLength(256).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256);
        builder.Property(user => user.DisplayName).HasMaxLength(256);
        builder.Property(user => user.PhoneNumber).HasMaxLength(32);

        // Sign-in name and e-mail address are looked up constantly, so both are indexed, and
        // both must be unique across every user.
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail);

        // Both appearance-preference overrides are nullable (null inherits AppearanceOptions's
        // own global default) and stored as their own enum member name, the same
        // string-conversion convention ZoneConfiguration already uses for ZoneEntity.Type.
        builder.Property(user => user.PreferredPalette).HasConversion<string>().HasMaxLength(32);
        builder.Property(user => user.PreferredMode).HasConversion<string>().HasMaxLength(32);

        // Both external-identity fields are nullable (null for every local account) and bounded
        // in length -- ExternalIdentityProvider is a short fixed literal ("Oidc" today, room for
        // a second provider kind later); ExternalSubjectId is an opaque, provider-issued
        // identifier that can legally be as long as a JWT "sub" claim ever gets.
        builder.Property(user => user.ExternalIdentityProvider).HasMaxLength(64);
        builder.Property(user => user.ExternalSubjectId).HasMaxLength(256);

        // OidcUserProvisioningService looks a user up by the combination of both fields on every
        // sign-in, and relies on that combination being unique so two different external
        // accounts can never collide onto the same local user. No explicit filter is needed to
        // exempt local accounts (where both columns are null) from that uniqueness constraint --
        // every relational provider this project supports (SQLite, PostgreSQL, MySQL, SQL Server)
        // already treats NULL as distinct from any other value, including another NULL, inside a
        // unique index, so any number of local accounts can coexist with both columns unset.
        builder
            .HasIndex(user => new { user.ExternalIdentityProvider, user.ExternalSubjectId })
            .IsUnique();

        // Null for every ordinary account -- see UserEntity.ClusterConflictNote's own doc comment
        // for when the cluster's own identity/RBAC federation mechanism sets this.
        builder.Property(user => user.ClusterConflictNote).HasMaxLength(1024);
    }
}

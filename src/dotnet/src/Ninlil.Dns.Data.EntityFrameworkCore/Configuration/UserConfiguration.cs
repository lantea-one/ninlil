using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="UserEntity" /> onto the <c>NinlilDnsUsers</c> table. Table names
///     follow one convention: a <c>NinlilDns</c> (or <c>NinlilDhcp</c>) prefix and the plural
///     entity name in UpperCamelCase. Columns keep the Entity Framework Core default of the
///     property name.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("NinlilDnsUsers");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.UserName).HasMaxLength(256).IsRequired();
        builder.Property(user => user.NormalizedUserName).HasMaxLength(256).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256);
        builder.Property(user => user.DisplayName).HasMaxLength(256);
        builder.Property(user => user.PhoneNumber).HasMaxLength(32);

        // Sign-in name and e-mail are looked up constantly. The sign-in name is unique here;
        // e-mail uniqueness is enforced by Identity (RequireUniqueEmail), not by the index.
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail);

        // Null uses the AppearanceOptions default.
        builder.Property(user => user.PreferredPalette).HasConversion<string>().HasMaxLength(32);
        builder.Property(user => user.PreferredMode).HasConversion<string>().HasMaxLength(32);

        // Both are null for local accounts. ExternalIdentityProvider is a short literal
        // ("Oidc"); ExternalSubjectId is a provider-issued "sub" value of up to 256 characters.
        builder.Property(user => user.ExternalIdentityProvider).HasMaxLength(64);
        builder.Property(user => user.ExternalSubjectId).HasMaxLength(256);

        // OidcUserProvisioningService looks users up by this pair on every sign-in and relies on
        // it being unique, so two external accounts never map to one local user. Local accounts
        // (both null) are not affected: SQLite, PostgreSQL and MySQL treat NULLs as distinct in a
        // unique index, and the SQL Server provider adds an IS NOT NULL filter to it.
        builder
            .HasIndex(user => new { user.ExternalIdentityProvider, user.ExternalSubjectId })
            .IsUnique();

        // Null for an ordinary account; see UserEntity.ClusterConflictNote.
        builder.Property(user => user.ClusterConflictNote).HasMaxLength(1024);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="ApiTokenEntity" /> onto the <c>NinlilDnsApiTokens</c> table.
/// </summary>
public class ApiTokenConfiguration : IEntityTypeConfiguration<ApiTokenEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<ApiTokenEntity> builder)
    {
        builder.ToTable("NinlilDnsApiTokens");

        builder.HasKey(token => token.Id);

        // The hash is unique so authenticating a request always finds exactly one token.
        builder.Property(token => token.TokenHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();

        // A user's tokens are listed together.
        builder.HasIndex(token => token.UserId);

        builder.Property(token => token.Name).HasMaxLength(256).IsRequired();

        builder.Property(token => token.Type).HasConversion<string>().HasMaxLength(32);

        // Deleting a user deletes their tokens.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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
        // Map this entity onto the NinlilDnsApiTokens table.
        builder.ToTable("NinlilDnsApiTokens");

        // The Id property is the primary key.
        builder.HasKey(token => token.Id);

        // The token hash is required and must be unique so that authenticating a request always
        // resolves to exactly one token.
        builder.Property(token => token.TokenHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();

        // Every token belonging to a user is listed together, so index the owning user.
        builder.HasIndex(token => token.UserId);

        builder.Property(token => token.Name).HasMaxLength(256).IsRequired();

        // Store the token type using its short, human-readable name rather than its numeric
        // value, so the raw table contents remain self-explanatory without this mapping code.
        builder.Property(token => token.Type).HasConversion<string>().HasMaxLength(32);

        // Deleting a user should clean up the tokens that reference it.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

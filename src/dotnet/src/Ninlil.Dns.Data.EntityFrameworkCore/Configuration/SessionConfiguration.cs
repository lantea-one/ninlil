using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="SessionEntity" /> onto the <c>NinlilDnsSessions</c> table.
/// </summary>
public class SessionConfiguration : IEntityTypeConfiguration<SessionEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<SessionEntity> builder)
    {
        // Map this entity onto the NinlilDnsSessions table.
        builder.ToTable("NinlilDnsSessions");

        // The Id property is the primary key.
        builder.HasKey(session => session.Id);

        // The refresh token hash is required and must be unique so that redeeming one always
        // resolves to exactly one session.
        builder.Property(session => session.RefreshTokenHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(session => session.RefreshTokenHash).IsUnique();

        // Every session belonging to a user is listed together, so index the owning user.
        builder.HasIndex(session => session.UserId);

        builder.Property(session => session.IpAddress).HasMaxLength(64);
        builder.Property(session => session.UserAgent).HasMaxLength(1024);

        // Deleting a user should clean up the sessions that reference it.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

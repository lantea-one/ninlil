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
        builder.ToTable("NinlilDnsSessions");

        builder.HasKey(session => session.Id);

        // The hash is unique so redeeming a refresh token always finds exactly one session.
        builder.Property(session => session.RefreshTokenHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(session => session.RefreshTokenHash).IsUnique();

        // A user's sessions are listed together.
        builder.HasIndex(session => session.UserId);

        builder.Property(session => session.IpAddress).HasMaxLength(64);
        builder.Property(session => session.UserAgent).HasMaxLength(1024);

        // Deleting a user deletes their sessions.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DhcpScopeCustomOptionEntity" /> onto the
///     <c>NinlilDhcpScopeCustomOptions</c> table.
/// </summary>
public class DhcpScopeCustomOptionConfiguration
    : IEntityTypeConfiguration<DhcpScopeCustomOptionEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DhcpScopeCustomOptionEntity> builder)
    {
        builder.ToTable("NinlilDhcpScopeCustomOptions");

        builder.HasKey(customOption => customOption.Id);

        builder.Property(customOption => customOption.ValueHex).HasMaxLength(1024).IsRequired();
        builder.Property(customOption => customOption.Comments).HasMaxLength(4096);

        // The database default of "Custom" means a row inserted some other way (raw SQL, for
        // example) still gets a valid value instead of NULL.
        builder
            .Property(customOption => customOption.ValueSource)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(DhcpOptionValueSourceEnum.Custom);

        // One value per tag per scope; a second would leave it unclear which the client gets.
        builder
            .HasIndex(customOption => new { customOption.ScopeId, customOption.OptionTag })
            .IsUnique();

        // Deleting a scope deletes its custom options.
        builder
            .HasOne<DhcpScopeEntity>()
            .WithMany()
            .HasForeignKey(customOption => customOption.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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
        // Map this entity onto the NinlilDhcpScopeCustomOptions table.
        builder.ToTable("NinlilDhcpScopeCustomOptions");

        // The Id property is the primary key.
        builder.HasKey(customOption => customOption.Id);

        builder.Property(customOption => customOption.ValueHex).HasMaxLength(1024).IsRequired();
        builder.Property(customOption => customOption.Comments).HasMaxLength(4096);

        // Stored as its own string name (matching DnsClientEntity.Origin's own established
        // enum-as-string convention), with a real DB-level default of "Custom" -- today's only
        // behavior -- so a row inserted through any path other than this project's own C# object
        // initializer (a raw SQL insert, for example) still lands as Custom rather than an
        // ambiguous NULL/zero value.
        builder
            .Property(customOption => customOption.ValueSource)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(DhcpOptionValueSourceEnum.Custom);

        // A scope may only configure one custom option per tag -- a second value for the same tag
        // would be ambiguous about which one a client should actually receive.
        builder
            .HasIndex(customOption => new { customOption.ScopeId, customOption.OptionTag })
            .IsUnique();

        // Deleting a scope should clean up the custom options that belong to it.
        builder
            .HasOne<DhcpScopeEntity>()
            .WithMany()
            .HasForeignKey(customOption => customOption.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

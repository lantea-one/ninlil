using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DhcpTagRuleEntity" /> onto the <c>NinlilDhcpTagRules</c> table.
/// </summary>
public class DhcpTagRuleConfiguration : IEntityTypeConfiguration<DhcpTagRuleEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DhcpTagRuleEntity> builder)
    {
        builder.ToTable("NinlilDhcpTagRules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Name).HasMaxLength(256).IsRequired();
        builder.Property(rule => rule.VendorClassIdentifierMatch).HasMaxLength(256);
        builder.Property(rule => rule.UserClassMatch).HasMaxLength(256);
        builder.Property(rule => rule.RelayAgentCircuitIdHex).HasMaxLength(512);
        builder.Property(rule => rule.RelayAgentRemoteIdHex).HasMaxLength(512);
        builder.Property(rule => rule.RelayInterfaceIdHex).HasMaxLength(512);
        builder.Property(rule => rule.Comments).HasMaxLength(4096);

        builder
            .Property(rule => rule.VendorClassIdentifierMatchMode)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(rule => rule.UserClassMatchMode).HasConversion<string>().HasMaxLength(16);

        // Stored as a signed long because not every provider has unsigned columns.
        builder.Property(rule => rule.VendorClassEnterpriseNumber).HasConversion<long?>();

        // ScopeId is optional (null means the rule applies to every scope), so this foreign key
        // is nullable, unlike those of reservations and custom options.
        builder
            .HasOne<DhcpScopeEntity>()
            .WithMany()
            .HasForeignKey(rule => rule.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

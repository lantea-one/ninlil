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
        // Map this entity onto the NinlilDhcpTagRules table.
        builder.ToTable("NinlilDhcpTagRules");

        // The Id property is the primary key.
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

        // Mirrors RecordEntity.TimeToLiveSeconds's own uint-via-long conversion -- not every
        // relational provider this project supports maps an unsigned column natively.
        builder.Property(rule => rule.VendorClassEnterpriseNumber).HasConversion<long?>();

        // A rule's own ScopeId is optional (null means globally eligible), so this is a nullable
        // foreign key with no required navigation -- unlike DhcpReservationEntity/
        // DhcpScopeCustomOptionEntity, which always belong to exactly one scope.
        builder
            .HasOne<DhcpScopeEntity>()
            .WithMany()
            .HasForeignKey(rule => rule.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

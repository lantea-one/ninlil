using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DhcpTagRuleOptionEntity" /> onto the
///     <c>NinlilDhcpTagRuleOptions</c> table.
/// </summary>
public class DhcpTagRuleOptionConfiguration : IEntityTypeConfiguration<DhcpTagRuleOptionEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DhcpTagRuleOptionEntity> builder)
    {
        // Map this entity onto the NinlilDhcpTagRuleOptions table.
        builder.ToTable("NinlilDhcpTagRuleOptions");

        // The Id property is the primary key.
        builder.HasKey(ruleOption => ruleOption.Id);

        builder.Property(ruleOption => ruleOption.ValueHex).HasMaxLength(1024).IsRequired();

        // The same enum-as-string-with-a-real-DB-default convention
        // DhcpScopeCustomOptionConfiguration establishes for its own identical ValueSource
        // property.
        builder
            .Property(ruleOption => ruleOption.ValueSource)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(DhcpOptionValueSourceEnum.Custom);

        // A rule may only configure one value per tag, the same uniqueness shape
        // DhcpScopeCustomOptionConfiguration already establishes for a scope's own custom options.
        builder
            .HasIndex(ruleOption => new { ruleOption.TagRuleId, ruleOption.OptionTag })
            .IsUnique();

        // Deleting a rule should clean up the options that belong to it.
        builder
            .HasOne<DhcpTagRuleEntity>()
            .WithMany()
            .HasForeignKey(ruleOption => ruleOption.TagRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

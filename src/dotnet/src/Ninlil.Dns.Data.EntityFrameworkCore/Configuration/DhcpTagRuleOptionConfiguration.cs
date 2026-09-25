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
        builder.ToTable("NinlilDhcpTagRuleOptions");

        builder.HasKey(ruleOption => ruleOption.Id);

        builder.Property(ruleOption => ruleOption.ValueHex).HasMaxLength(1024).IsRequired();

        // Same database default as DhcpScopeCustomOptionConfiguration.ValueSource.
        builder
            .Property(ruleOption => ruleOption.ValueSource)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(DhcpOptionValueSourceEnum.Custom);

        // One value per tag per rule.
        builder
            .HasIndex(ruleOption => new { ruleOption.TagRuleId, ruleOption.OptionTag })
            .IsUnique();

        // Deleting a rule deletes its options.
        builder
            .HasOne<DhcpTagRuleEntity>()
            .WithMany()
            .HasForeignKey(ruleOption => ruleOption.TagRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsAccessControlRuleEntity" /> onto the
///     <c>NinlilDnsAccessControlRules</c> table. <see cref="DnsAccessControlRuleEntity.Capabilities" />
///     has no conversion: as a flags enum it is stored as an <c>int</c>, so a new capability bit
///     needs no schema change.
/// </summary>
public class DnsAccessControlRuleConfiguration
    : IEntityTypeConfiguration<DnsAccessControlRuleEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsAccessControlRuleEntity> builder)
    {
        builder.ToTable("NinlilDnsAccessControlRules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Name).HasMaxLength(256).IsRequired();
        builder.Property(rule => rule.Cidr).HasMaxLength(64).IsRequired();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsAccessControlRuleEntity" /> onto the
///     <c>NinlilDnsAccessControlRules</c> table. <see cref="DnsAccessControlRuleEntity.Capabilities" />
///     needs no explicit <c>HasConversion</c> call -- a <see cref="System.FlagsAttribute" /> enum
///     maps to a plain <c>int</c> column under Entity Framework Core's own default convention, the
///     same reason its own doc comment gives for why adding a third capability bit later needs no
///     schema change on any relational provider.
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

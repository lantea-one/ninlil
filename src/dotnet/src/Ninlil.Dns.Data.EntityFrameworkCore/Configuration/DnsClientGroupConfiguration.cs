using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsClientGroupEntity" /> onto the
///     <c>NinlilDnsClientGroups</c> table.
/// </summary>
public class DnsClientGroupConfiguration : IEntityTypeConfiguration<DnsClientGroupEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsClientGroupEntity> builder)
    {
        builder.ToTable("NinlilDnsClientGroups");

        builder.HasKey(group => group.Id);

        // Group names are unique so operators can tell groups apart.
        builder.Property(group => group.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(group => group.Name).IsUnique();
    }
}

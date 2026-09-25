using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsFilterListEntity" /> onto the
///     <c>NinlilDnsFilterLists</c> table.
/// </summary>
public class DnsFilterListConfiguration : IEntityTypeConfiguration<DnsFilterListEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsFilterListEntity> builder)
    {
        builder.ToTable("NinlilDnsFilterLists");

        builder.HasKey(list => list.Id);

        // List names are unique.
        builder.Property(list => list.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(list => list.Name).IsUnique();

        builder.Property(list => list.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(list => list.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(list => list.Source).HasConversion<string>().HasMaxLength(32);

        builder.Property(list => list.SubscriptionUrl).HasMaxLength(2048);
        builder.Property(list => list.Comments).HasMaxLength(4096);
    }
}

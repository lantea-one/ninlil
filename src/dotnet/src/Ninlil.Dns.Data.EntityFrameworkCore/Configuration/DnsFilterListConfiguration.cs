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
        // Map this entity onto the NinlilDnsFilterLists table.
        builder.ToTable("NinlilDnsFilterLists");

        // The Id property is the primary key.
        builder.HasKey(list => list.Id);

        // A list's name is required and unique, the same way a TSIG key's name must be unique.
        builder.Property(list => list.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(list => list.Name).IsUnique();

        // Store each enum using its short, human-readable name rather than its numeric value.
        builder.Property(list => list.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(list => list.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(list => list.Source).HasConversion<string>().HasMaxLength(32);

        builder.Property(list => list.SubscriptionUrl).HasMaxLength(2048);
        builder.Property(list => list.Comments).HasMaxLength(4096);
    }
}

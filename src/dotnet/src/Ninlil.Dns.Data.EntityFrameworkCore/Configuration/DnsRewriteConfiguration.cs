using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DnsRewriteEntity" /> onto the <c>NinlilDnsRewrites</c>
///     table.
/// </summary>
public class DnsRewriteConfiguration : IEntityTypeConfiguration<DnsRewriteEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DnsRewriteEntity> builder)
    {
        builder.ToTable("NinlilDnsRewrites");

        builder.HasKey(rewrite => rewrite.Id);

        // Not unique: a domain can have both an A-target and an AAAA-target rewrite.
        builder.Property(rewrite => rewrite.Domain).HasMaxLength(255).IsRequired();
        builder.HasIndex(rewrite => rewrite.Domain);

        builder.Property(rewrite => rewrite.Target).HasMaxLength(255).IsRequired();
        builder.Property(rewrite => rewrite.Comments).HasMaxLength(4096);

        // DnsRewriteEngine matches a resolved client and its group against these columns.
        builder.HasIndex(rewrite => rewrite.ClientId);
        builder.HasIndex(rewrite => rewrite.ClientGroupId);

        // Stored as UTC ticks so SearchAsync can order by it on SQLite; see NinlilDnsContext.
        builder
            .Property(rewrite => rewrite.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

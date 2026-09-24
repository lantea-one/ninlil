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

        // Deliberately not unique -- see DnsRewriteEntity.Domain's own doc comment: an operator
        // may legitimately configure separate A-target and AAAA-target rewrites for the same
        // domain, so a naive unique index on Domain alone would reject that.
        builder.Property(rewrite => rewrite.Domain).HasMaxLength(255).IsRequired();
        builder.HasIndex(rewrite => rewrite.Domain);

        builder.Property(rewrite => rewrite.Target).HasMaxLength(255).IsRequired();
        builder.Property(rewrite => rewrite.Comments).HasMaxLength(4096);

        // DnsRewriteEngine's own client-derived scoping resolution looks a matched client's own
        // identifier/group up against these two columns -- indexed the same way every other
        // scoping join in this project already is (see DnsClientScheduleConfiguration.ClientId),
        // even though most deployments will only ever hold a handful of rewrite rows.
        builder.HasIndex(rewrite => rewrite.ClientId);
        builder.HasIndex(rewrite => rewrite.ClientGroupId);

        // Stored as UTC ticks rather than the entity's own DateTimeOffset shape directly:
        // SQLite's own EF Core provider refuses to translate an ORDER BY over a DateTimeOffset
        // column at all, which SearchAsync's own domain-then-creation-time paging needs. Every
        // value this column ever holds is already UTC, so converting to a plain UTC tick count
        // loses nothing and orders correctly and efficiently on every one of this project's 4
        // relational providers -- the same conversion DnsQueryLogEntryConfiguration.OccurredAtUtc
        // and DnssecKeyConfiguration.CreatedAtUtc already establish for the identical shape of
        // problem.
        builder
            .Property(rewrite => rewrite.CreatedAtUtc)
            .HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero)
            );
    }
}

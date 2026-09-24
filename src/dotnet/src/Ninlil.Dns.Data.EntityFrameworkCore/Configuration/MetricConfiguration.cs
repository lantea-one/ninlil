using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="MetricEntity" /> onto the <c>NinlilDnsMetrics</c> table.
/// </summary>
public class MetricConfiguration : IEntityTypeConfiguration<MetricEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<MetricEntity> builder)
    {
        // Map this entity onto the NinlilDnsMetrics table.
        builder.ToTable("NinlilDnsMetrics");

        // The Id property is the primary key.
        builder.HasKey(metric => metric.Id);

        builder.Property(metric => metric.Category).HasMaxLength(128).IsRequired();
        builder.Property(metric => metric.Name).HasMaxLength(256).IsRequired();

        // The dashboard always asks for one named metric across a time range, so index exactly
        // that access pattern.
        builder.HasIndex(metric => new
        {
            metric.Category,
            metric.Name,
            metric.TimestampUtc,
        });
    }
}

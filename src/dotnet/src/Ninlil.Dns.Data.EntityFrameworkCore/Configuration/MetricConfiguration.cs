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
        builder.ToTable("NinlilDnsMetrics");

        builder.HasKey(metric => metric.Id);

        builder.Property(metric => metric.Category).HasMaxLength(128).IsRequired();
        builder.Property(metric => metric.Name).HasMaxLength(256).IsRequired();

        // The dashboard asks for one named metric over a time range.
        builder.HasIndex(metric => new
        {
            metric.Category,
            metric.Name,
            metric.TimestampUtc,
        });
    }
}

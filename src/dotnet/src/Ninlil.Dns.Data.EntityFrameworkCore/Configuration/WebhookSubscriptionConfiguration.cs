using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="WebhookSubscriptionEntity" /> onto the
///     <c>NinlilDnsWebhookSubscriptions</c> table.
/// </summary>
public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscriptionEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<WebhookSubscriptionEntity> builder)
    {
        builder.ToTable("NinlilDnsWebhookSubscriptions");

        builder.HasKey(subscription => subscription.Id);

        // Subscription names are unique.
        builder.Property(subscription => subscription.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(subscription => subscription.Name).IsUnique();

        builder.Property(subscription => subscription.TargetUrl).HasMaxLength(2048).IsRequired();

        builder
            .Property(subscription => subscription.EventTypeNames)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(subscription => subscription.AuthMode)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(subscription => subscription.Comments).HasMaxLength(4096);
    }
}

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
        // Map this entity onto the NinlilDnsWebhookSubscriptions table.
        builder.ToTable("NinlilDnsWebhookSubscriptions");

        // The Id property is the primary key.
        builder.HasKey(subscription => subscription.Id);

        // A subscription's name is required and unique, the same way a TSIG key's name must be
        // unique.
        builder.Property(subscription => subscription.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(subscription => subscription.Name).IsUnique();

        builder.Property(subscription => subscription.TargetUrl).HasMaxLength(2048).IsRequired();

        // Store the event-type-name list as JSON, the same reusable converter/comparer pair
        // ZoneEntity's own address lists already use -- none of the four relational providers
        // Ninlil supports offer a portable native array column type.
        builder
            .Property(subscription => subscription.EventTypeNames)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        // Store the auth mode using its short, human-readable name rather than its numeric value,
        // matching every other enum column this project stores.
        builder
            .Property(subscription => subscription.AuthMode)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(subscription => subscription.Comments).HasMaxLength(4096);
    }
}

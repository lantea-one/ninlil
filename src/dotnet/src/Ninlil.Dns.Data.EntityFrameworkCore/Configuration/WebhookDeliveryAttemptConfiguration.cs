using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="WebhookDeliveryAttemptEntity" /> onto the
///     <c>NinlilDnsWebhookDeliveryAttempts</c> table, with the indexes
///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IWebhookDeliveryAttemptRepository" />'s
///     own real search/retention queries rely on -- mirroring
///     <see cref="DnsQueryLogEntryConfiguration" />'s own reasoning for a table that can genuinely
///     grow large over a long-running deployment.
/// </summary>
public class WebhookDeliveryAttemptConfiguration
    : IEntityTypeConfiguration<WebhookDeliveryAttemptEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<WebhookDeliveryAttemptEntity> builder)
    {
        // Map this entity onto the NinlilDnsWebhookDeliveryAttempts table.
        builder.ToTable("NinlilDnsWebhookDeliveryAttempts");

        // The Id property is the primary key.
        builder.HasKey(attempt => attempt.Id);

        // A retry sweep always narrows by subscription indirectly (it reads every subscription's
        // own delivery attempts) and, more importantly, by due status/time -- both indexed below.
        builder.HasIndex(attempt => attempt.SubscriptionId);

        builder.Property(attempt => attempt.SubscriptionName).HasMaxLength(255);

        builder.Property(attempt => attempt.EventTypeName).HasMaxLength(128);

        // Store the delivery status using its short, human-readable name rather than its numeric
        // value, matching every other enum column this project stores.
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(attempt => attempt.LastErrorMessage).HasMaxLength(4096);

        // Stored as UTC ticks rather than these two properties' own DateTimeOffset shape directly
        // -- the exact same conversion, and the exact same reason, DnsQueryLogEntryConfiguration's
        // own OccurredAtUtc column already establishes: SQLite's own EF Core provider cannot
        // correctly compare a DateTimeOffset column's default text representation once two rows
        // could carry different offsets, which both WebhookDeliveryRetrySweepService's own
        // due-for-retry comparison (NextRetryDueUtc) and its own retention sweep's age comparison
        // (CompletedAtUtc) rely on. Every value either column ever holds is already UTC
        // (WebhookDeliveryService never stores anything else), so this conversion loses nothing.
        builder
            .Property(attempt => attempt.NextRetryDueUtc)
            .HasConversion(
                value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null
            );

        builder
            .Property(attempt => attempt.CompletedAtUtc)
            .HasConversion(
                value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null
            );

        // WebhookDeliveryRetrySweepService's own due-for-retry query filters on Status and orders
        // by nothing beyond that (no ordering requirement exists -- every due row is attempted in
        // the same sweep pass), so a composite index covering both the status filter and the
        // NextRetryDueUtc comparison serves that query directly.
        builder.HasIndex(attempt => new { attempt.Status, attempt.NextRetryDueUtc });

        // The retention sweep's own age comparison filters on this column for terminal rows only,
        // so it gets its own index independent of the composite above (a DeadLettered/Succeeded
        // row's own NextRetryDueUtc is always null by the time CompletedAtUtc is set).
        builder.HasIndex(attempt => attempt.CompletedAtUtc);
    }
}

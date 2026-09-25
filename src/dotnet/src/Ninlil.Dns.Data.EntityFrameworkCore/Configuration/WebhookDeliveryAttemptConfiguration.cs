using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="WebhookDeliveryAttemptEntity" /> onto the
///     <c>NinlilDnsWebhookDeliveryAttempts</c> table, with the indexes the retry and retention
///     queries of <see cref="Ninlil.Dns.Data.Abstractions.Repository.IWebhookDeliveryAttemptRepository" />
///     rely on. Like <see cref="DnsQueryLogEntryConfiguration" />, the table can grow large.
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
        builder.ToTable("NinlilDnsWebhookDeliveryAttempts");

        builder.HasKey(attempt => attempt.Id);

        // Attempts are looked up by subscription.
        builder.HasIndex(attempt => attempt.SubscriptionId);

        builder.Property(attempt => attempt.SubscriptionName).HasMaxLength(255);

        builder.Property(attempt => attempt.EventTypeName).HasMaxLength(128);

        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(attempt => attempt.LastErrorMessage).HasMaxLength(4096);

        // Stored as UTC ticks so the due-for-retry comparison (NextRetryDueUtc) and the retention
        // comparison (CompletedAtUtc) work on SQLite; see NinlilDnsContext.
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

        // The due-for-retry query filters on Status and compares NextRetryDueUtc, with no
        // ordering, so one composite index serves it.
        builder.HasIndex(attempt => new { attempt.Status, attempt.NextRetryDueUtc });

        // The retention sweep filters terminal rows on this column. It needs its own index
        // because a terminal row's NextRetryDueUtc is null.
        builder.HasIndex(attempt => attempt.CompletedAtUtc);
    }
}

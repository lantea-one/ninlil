using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class ConvertWebhookAttemptCreatedAtToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts");

        // MySQL's own datetime-to-bigint ALTER yields a YYYYMMDDhhmmss number rather than ticks,
        // so the ticks go into a new column that replaces the old one.
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtUtcTicks",
            table: "NinlilDnsWebhookDeliveryAttempts",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsWebhookDeliveryAttempts` SET `CreatedAtUtcTicks` = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', `CreatedAtUtc`) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.RenameColumn(
            name: "CreatedAtUtcTicks",
            table: "NinlilDnsWebhookDeliveryAttempts",
            newName: "CreatedAtUtc"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId_CreatedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts",
            columns: new[] { "SubscriptionId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId_CreatedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CreatedAtUtcValue",
            table: "NinlilDnsWebhookDeliveryAttempts",
            type: "datetime(6)",
            nullable: false,
            defaultValue: new DateTimeOffset(
                new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                new TimeSpan(0, 0, 0, 0, 0)
            )
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsWebhookDeliveryAttempts` SET `CreatedAtUtcValue` = DATE_ADD('1970-01-01 00:00:00.000000', INTERVAL (`CreatedAtUtc` - 621355968000000000) DIV 10 MICROSECOND);
            """
        );

        migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.RenameColumn(
            name: "CreatedAtUtcValue",
            table: "NinlilDnsWebhookDeliveryAttempts",
            newName: "CreatedAtUtc"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts",
            column: "SubscriptionId");
    }
}

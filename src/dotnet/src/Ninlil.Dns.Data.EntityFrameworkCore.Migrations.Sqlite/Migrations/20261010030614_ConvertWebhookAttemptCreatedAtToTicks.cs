using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class ConvertWebhookAttemptCreatedAtToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Raw SQL runs before the AlterColumn call: EF defers SQLite's table rebuild past any
        // raw SQL, so the text is converted to UTC ticks (millisecond precision, via julianday)
        // while the column is still TEXT.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDnsWebhookDeliveryAttempts" SET "CreatedAtUtc" = CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000;
            """
        );

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.AlterColumn<long>(
            name: "CreatedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "TEXT");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId_CreatedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts",
            columns: new[] { "SubscriptionId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Ticks back to ISO-8601 text before the column is rebuilt as TEXT.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDnsWebhookDeliveryAttempts" SET "CreatedAtUtc" = strftime('%Y-%m-%d %H:%M:%f', ("CreatedAtUtc" - 621355968000000000) / 10000000.0, 'unixepoch') || '+00:00';
            """
        );

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId_CreatedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts",
            column: "SubscriptionId");
    }
}

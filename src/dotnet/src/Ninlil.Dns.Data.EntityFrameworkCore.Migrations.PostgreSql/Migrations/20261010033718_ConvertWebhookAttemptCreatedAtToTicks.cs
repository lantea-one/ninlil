using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class ConvertWebhookAttemptCreatedAtToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts");

        // A plain AlterColumn fails on PostgreSQL (no implicit timestamptz-to-bigint cast),
        // so the USING expression converts to UTC ticks (microsecond precision).
        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDnsWebhookDeliveryAttempts"
            ALTER COLUMN "CreatedAtUtc" TYPE bigint
            USING ((EXTRACT(EPOCH FROM "CreatedAtUtc") * 1000000)::bigint * 10 + 621355968000000000);
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookAttempts_SubscriptionId_CreatedAtUtc_Id",
            table: "NinlilDnsWebhookDeliveryAttempts",
            columns: new[] { "SubscriptionId", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookAttempts_SubscriptionId_CreatedAtUtc_Id",
            table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDnsWebhookDeliveryAttempts"
            ALTER COLUMN "CreatedAtUtc" TYPE timestamp with time zone
            USING (to_timestamp((("CreatedAtUtc" - 621355968000000000) / 10)::double precision / 1000000.0));
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts",
            column: "SubscriptionId");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class ConvertWebhookAttemptCreatedAtToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts");

        // SQL Server refuses to convert datetimeoffset to bigint, so the ticks go into a new column
        // that replaces the old one.
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtUtcTicks",
            table: "NinlilDnsWebhookDeliveryAttempts",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsWebhookDeliveryAttempts] SET [CreatedAtUtcTicks] = DATEDIFF_BIG(MICROSECOND, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset), [CreatedAtUtc]) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "NinlilDnsWebhookDeliveryAttempts");

        migrationBuilder.RenameColumn(
            name: "CreatedAtUtcTicks",
            table: "NinlilDnsWebhookDeliveryAttempts",
            newName: "CreatedAtUtc"
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

        // Ticks back to datetimeoffset. DATEADD takes an int, so the offset is applied as days,
        // seconds and microseconds.
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CreatedAtUtcValue",
            table: "NinlilDnsWebhookDeliveryAttempts",
            type: "datetimeoffset",
            nullable: false,
            defaultValue: new DateTimeOffset(
                new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                new TimeSpan(0, 0, 0, 0, 0)
            )
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsWebhookDeliveryAttempts] SET [CreatedAtUtcValue] = DATEADD(MICROSECOND, (([CreatedAtUtc] - 621355968000000000) / 10) % 1000000, DATEADD(SECOND, (([CreatedAtUtc] - 621355968000000000) / 10000000) % 86400, DATEADD(DAY, ([CreatedAtUtc] - 621355968000000000) / 864000000000, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset))));
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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddWebhookDeliveryAttempt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .CreateTable(
                name: "NinlilDnsWebhookDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    SubscriptionId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    SubscriptionName = table
                        .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventTypeName = table
                        .Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayloadJson = table
                        .Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    NextRetryDueUtc = table.Column<long>(type: "bigint", nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    LastErrorMessage = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedAtUtc = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsWebhookDeliveryAttempts", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_CompletedAtUtc",
            table: "NinlilDnsWebhookDeliveryAttempts",
            column: "CompletedAtUtc"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_Status_NextRetryDueUtc",
            table: "NinlilDnsWebhookDeliveryAttempts",
            columns: new[] { "Status", "NextRetryDueUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookDeliveryAttempts_SubscriptionId",
            table: "NinlilDnsWebhookDeliveryAttempts",
            column: "SubscriptionId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsWebhookDeliveryAttempts");
    }
}

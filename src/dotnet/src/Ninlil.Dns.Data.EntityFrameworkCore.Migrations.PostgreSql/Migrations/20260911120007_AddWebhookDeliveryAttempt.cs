using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDeliveryAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NinlilDnsWebhookDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionName = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: true
                    ),
                    EventTypeName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    NextRetryDueUtc = table.Column<long>(type: "bigint", nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastErrorMessage = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsWebhookDeliveryAttempts", x => x.Id);
                }
            );

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
}

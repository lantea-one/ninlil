using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionName = table.Column<string>(
                        type: "nvarchar(255)",
                        maxLength: 255,
                        nullable: true
                    ),
                    EventTypeName = table.Column<string>(
                        type: "nvarchar(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(
                        type: "nvarchar(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: true
                    ),
                    NextRetryDueUtc = table.Column<long>(type: "bigint", nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    LastErrorMessage = table.Column<string>(
                        type: "nvarchar(max)",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
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

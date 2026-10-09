using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddWebhookSubscriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsWebhookSubscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                TargetUrl = table.Column<string>(
                    type: "nvarchar(2048)",
                    maxLength: 2048,
                    nullable: false
                ),
                EventTypeNames = table.Column<string>(type: "nvarchar(max)", nullable: true),
                AuthMode = table.Column<string>(
                    type: "nvarchar(32)",
                    maxLength: 32,
                    nullable: false
                ),
                AuthCredentialsEncrypted = table.Column<string>(
                    type: "nvarchar(max)",
                    nullable: true
                ),
                HmacSigningSecretEncrypted = table.Column<string>(
                    type: "nvarchar(max)",
                    nullable: true
                ),
                MaxDeliveryAttempts = table.Column<int>(type: "int", nullable: false),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                Comments = table.Column<string>(
                    type: "nvarchar(max)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsWebhookSubscriptions", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsWebhookSubscriptions_Name",
            table: "NinlilDnsWebhookSubscriptions",
            column: "Name",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsWebhookSubscriptions");
    }
}

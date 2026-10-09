using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

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
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                TargetUrl = table.Column<string>(
                    type: "TEXT",
                    maxLength: 2048,
                    nullable: false
                ),
                EventTypeNames = table.Column<string>(type: "TEXT", nullable: true),
                AuthMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                AuthCredentialsEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                HmacSigningSecretEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                MaxDeliveryAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
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

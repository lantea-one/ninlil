using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDnsQueryLogEntryClientIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .AddColumn<string>(
                name: "ClientIdentifier",
                table: "NinlilDnsQueryLogEntries",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .AddColumn<string>(
                name: "ClientProtocolVersion",
                table: "NinlilDnsQueryLogEntries",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientIdentifier",
            table: "NinlilDnsQueryLogEntries",
            column: "ClientIdentifier"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientIdentifier",
            table: "NinlilDnsQueryLogEntries"
        );

        migrationBuilder.DropColumn(
            name: "ClientIdentifier",
            table: "NinlilDnsQueryLogEntries"
        );

        migrationBuilder.DropColumn(
            name: "ClientProtocolVersion",
            table: "NinlilDnsQueryLogEntries"
        );
    }
}

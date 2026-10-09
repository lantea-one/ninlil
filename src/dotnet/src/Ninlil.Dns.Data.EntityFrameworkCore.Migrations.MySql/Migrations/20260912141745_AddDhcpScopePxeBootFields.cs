using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopePxeBootFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .AddColumn<string>(
                name: "BootFileName",
                table: "NinlilDhcpScopes",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .AddColumn<string>(
                name: "NextServerAddress",
                table: "NinlilDhcpScopes",
                type: "varchar(45)",
                maxLength: 45,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BootFileName", table: "NinlilDhcpScopes");

        migrationBuilder.DropColumn(name: "NextServerAddress", table: "NinlilDhcpScopes");
    }
}

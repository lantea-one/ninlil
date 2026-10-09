using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddIdentityClusterConflictNote : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .AddColumn<string>(
                name: "ClusterConflictNote",
                table: "NinlilDnsUsers",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .AddColumn<string>(
                name: "ClusterConflictNote",
                table: "NinlilDnsRoles",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ClusterConflictNote", table: "NinlilDnsUsers");

        migrationBuilder.DropColumn(name: "ClusterConflictNote", table: "NinlilDnsRoles");
    }
}

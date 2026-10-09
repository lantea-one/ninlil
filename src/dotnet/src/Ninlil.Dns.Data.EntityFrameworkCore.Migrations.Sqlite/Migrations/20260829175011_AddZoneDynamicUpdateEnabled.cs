using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddZoneDynamicUpdateEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "DynamicUpdateEnabled",
            table: "NinlilDnsZones",
            type: "INTEGER",
            nullable: false,
            defaultValue: false
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DynamicUpdateEnabled", table: "NinlilDnsZones");
    }
}

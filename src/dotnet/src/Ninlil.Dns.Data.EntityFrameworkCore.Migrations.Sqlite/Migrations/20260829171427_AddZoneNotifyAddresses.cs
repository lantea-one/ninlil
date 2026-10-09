using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddZoneNotifyAddresses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NotifyAddresses",
            table: "NinlilDnsZones",
            type: "TEXT",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "NotifyAddresses", table: "NinlilDnsZones");
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDnsClientDeviceIconAndFavorite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .AddColumn<string>(
                name: "DeviceIconKey",
                table: "NinlilDnsClients",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<bool>(
            name: "IsFavorite",
            table: "NinlilDnsClients",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DeviceIconKey", table: "NinlilDnsClients");

        migrationBuilder.DropColumn(name: "IsFavorite", table: "NinlilDnsClients");
    }
}

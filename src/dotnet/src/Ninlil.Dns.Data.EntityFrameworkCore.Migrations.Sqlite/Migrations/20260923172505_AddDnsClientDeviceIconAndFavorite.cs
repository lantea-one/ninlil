using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsClientDeviceIconAndFavorite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceIconKey",
                table: "NinlilDnsClients",
                type: "TEXT",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "NinlilDnsClients",
                type: "INTEGER",
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
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneTsigKeyBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransferTsigKeyNames",
                table: "NinlilDnsZones",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdateTsigKeyNames",
                table: "NinlilDnsZones",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransferTsigKeyNames",
                table: "NinlilDnsZones");

            migrationBuilder.DropColumn(
                name: "UpdateTsigKeyNames",
                table: "NinlilDnsZones");
        }
    }
}

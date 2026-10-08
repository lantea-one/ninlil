using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
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
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "UpdateTsigKeyNames",
                table: "NinlilDnsZones",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
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

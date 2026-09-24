using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAppearancePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredMode",
                table: "NinlilDnsUsers",
                type: "TEXT",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PreferredPalette",
                table: "NinlilDnsUsers",
                type: "TEXT",
                maxLength: 32,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PreferredMode", table: "NinlilDnsUsers");

            migrationBuilder.DropColumn(name: "PreferredPalette", table: "NinlilDnsUsers");
        }
    }
}

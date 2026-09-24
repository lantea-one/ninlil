using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAppearancePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AddColumn<string>(
                    name: "PreferredMode",
                    table: "NinlilDnsUsers",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .AddColumn<string>(
                    name: "PreferredPalette",
                    table: "NinlilDnsUsers",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PreferredMode", table: "NinlilDnsUsers");

            migrationBuilder.DropColumn(name: "PreferredPalette", table: "NinlilDnsUsers");
        }
    }
}

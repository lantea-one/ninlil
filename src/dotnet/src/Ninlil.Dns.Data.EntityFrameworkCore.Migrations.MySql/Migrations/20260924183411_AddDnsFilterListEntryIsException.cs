using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsFilterListEntryIsException : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsException",
                table: "NinlilDnsFilterListEntries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsException", table: "NinlilDnsFilterListEntries");
        }
    }
}

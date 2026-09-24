using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDhcpScopePxeBootFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BootFileName",
                table: "NinlilDhcpScopes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "NextServerAddress",
                table: "NinlilDhcpScopes",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BootFileName", table: "NinlilDhcpScopes");

            migrationBuilder.DropColumn(name: "NextServerAddress", table: "NinlilDhcpScopes");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDhcpv6ScopeKindAndPrefixDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DelegatedPrefixLength",
                table: "NinlilDhcpScopes",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "NinlilDhcpScopes",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "AddressAssignment"
            );

            migrationBuilder.AddColumn<int>(
                name: "PrefixLength",
                table: "NinlilDhcpLeases",
                type: "INTEGER",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DelegatedPrefixLength", table: "NinlilDhcpScopes");

            migrationBuilder.DropColumn(name: "Kind", table: "NinlilDhcpScopes");

            migrationBuilder.DropColumn(name: "PrefixLength", table: "NinlilDhcpLeases");
        }
    }
}

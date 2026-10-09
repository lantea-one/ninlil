using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopeOptionAddresses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DnsServerAddresses",
            table: "NinlilDhcpScopes",
            type: "nvarchar(max)",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "GatewayAddresses",
            table: "NinlilDhcpScopes",
            type: "nvarchar(max)",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DnsServerAddresses", table: "NinlilDhcpScopes");

        migrationBuilder.DropColumn(name: "GatewayAddresses", table: "NinlilDhcpScopes");
    }
}

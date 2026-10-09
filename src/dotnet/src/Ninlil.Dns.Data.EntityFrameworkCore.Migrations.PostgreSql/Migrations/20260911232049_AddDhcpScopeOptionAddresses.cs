using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddDhcpScopeOptionAddresses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DnsServerAddresses",
            table: "NinlilDhcpScopes",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "GatewayAddresses",
            table: "NinlilDhcpScopes",
            type: "text",
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

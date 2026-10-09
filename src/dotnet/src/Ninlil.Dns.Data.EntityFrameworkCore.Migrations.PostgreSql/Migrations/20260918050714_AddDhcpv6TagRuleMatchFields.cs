using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddDhcpv6TagRuleMatchFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RelayInterfaceIdHex",
            table: "NinlilDhcpTagRules",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true
        );

        migrationBuilder.AddColumn<long>(
            name: "VendorClassEnterpriseNumber",
            table: "NinlilDhcpTagRules",
            type: "bigint",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RelayInterfaceIdHex", table: "NinlilDhcpTagRules");

        migrationBuilder.DropColumn(
            name: "VendorClassEnterpriseNumber",
            table: "NinlilDhcpTagRules"
        );
    }
}

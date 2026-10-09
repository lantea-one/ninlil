using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDhcpv6TagRuleMatchFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RelayInterfaceIdHex",
            table: "NinlilDhcpTagRules",
            type: "TEXT",
            maxLength: 512,
            nullable: true
        );

        migrationBuilder.AddColumn<long>(
            name: "VendorClassEnterpriseNumber",
            table: "NinlilDhcpTagRules",
            type: "INTEGER",
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

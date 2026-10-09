using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddDhcpOptionValueSource : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ValueSource",
            table: "NinlilDhcpTagRuleOptions",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Custom"
        );

        migrationBuilder.AddColumn<string>(
            name: "ValueSource",
            table: "NinlilDhcpScopeCustomOptions",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Custom"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ValueSource", table: "NinlilDhcpTagRuleOptions");

        migrationBuilder.DropColumn(name: "ValueSource", table: "NinlilDhcpScopeCustomOptions");
    }
}

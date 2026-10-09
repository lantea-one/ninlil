using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDhcpOptionValueSource : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ValueSource",
            table: "NinlilDhcpTagRuleOptions",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "Custom"
        );

        migrationBuilder.AddColumn<string>(
            name: "ValueSource",
            table: "NinlilDhcpScopeCustomOptions",
            type: "TEXT",
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

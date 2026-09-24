using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDhcpOptionValueSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AddColumn<string>(
                    name: "ValueSource",
                    table: "NinlilDhcpTagRuleOptions",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValue: "Custom"
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .AddColumn<string>(
                    name: "ValueSource",
                    table: "NinlilDhcpScopeCustomOptions",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValue: "Custom"
                )
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ValueSource", table: "NinlilDhcpTagRuleOptions");

            migrationBuilder.DropColumn(name: "ValueSource", table: "NinlilDhcpScopeCustomOptions");
        }
    }
}

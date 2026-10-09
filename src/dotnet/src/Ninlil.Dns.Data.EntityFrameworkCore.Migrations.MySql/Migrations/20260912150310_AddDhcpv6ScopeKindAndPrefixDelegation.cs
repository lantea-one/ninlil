using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDhcpv6ScopeKindAndPrefixDelegation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DelegatedPrefixLength",
            table: "NinlilDhcpScopes",
            type: "int",
            nullable: true
        );

        migrationBuilder
            .AddColumn<string>(
                name: "Kind",
                table: "NinlilDhcpScopes",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "AddressAssignment"
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<int>(
            name: "PrefixLength",
            table: "NinlilDhcpLeases",
            type: "int",
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

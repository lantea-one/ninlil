using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddDnsClientOrigin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Origin",
            table: "NinlilDnsClients",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Manual"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Origin", table: "NinlilDnsClients");
    }
}

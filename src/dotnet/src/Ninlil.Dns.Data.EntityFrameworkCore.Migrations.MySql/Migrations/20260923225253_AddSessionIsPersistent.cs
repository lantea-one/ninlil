using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddSessionIsPersistent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Every session that already exists was issued before browser-session sign-ins did,
        // so it is a long-lived one; backfilling true keeps it on the long refresh lifetime
        // when it next rotates. New rows always write the column explicitly.
        migrationBuilder.AddColumn<bool>(
            name: "IsPersistent",
            table: "NinlilDnsSessions",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsPersistent", table: "NinlilDnsSessions");
    }
}

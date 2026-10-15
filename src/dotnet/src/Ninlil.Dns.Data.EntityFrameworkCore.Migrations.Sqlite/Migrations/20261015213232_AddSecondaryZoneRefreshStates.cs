using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddSecondaryZoneRefreshStates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsSecondaryZoneRefreshStates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                LastRefreshedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsSecondaryZoneRefreshStates", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsSecondaryZoneRefreshStates_ZoneId",
            table: "NinlilDnsSecondaryZoneRefreshStates",
            column: "ZoneId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NinlilDnsSecondaryZoneRefreshStates");
    }
}

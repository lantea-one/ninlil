using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

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
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                LastRefreshedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDnsRewrites : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsRewrites",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Domain = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                Target = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsRewrites", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRewrites_Domain",
            table: "NinlilDnsRewrites",
            column: "Domain"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsRewrites");
    }
}

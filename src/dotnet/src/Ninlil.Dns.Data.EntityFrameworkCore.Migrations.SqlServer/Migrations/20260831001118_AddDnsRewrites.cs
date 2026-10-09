using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

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
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Domain = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                Target = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                Comments = table.Column<string>(
                    type: "nvarchar(max)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
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

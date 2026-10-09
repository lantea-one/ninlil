using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

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
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Domain = table.Column<string>(
                    type: "character varying(255)",
                    maxLength: 255,
                    nullable: false
                ),
                Target = table.Column<string>(
                    type: "character varying(255)",
                    maxLength: 255,
                    nullable: false
                ),
                IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                Comments = table.Column<string>(
                    type: "character varying(4096)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
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

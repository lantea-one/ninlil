using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class ConvertDnsRewriteCreatedAtUtcToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsFilterListEntries_ListId",
            table: "NinlilDnsFilterListEntries"
        );

        migrationBuilder.AlterColumn<long>(
            name: "CreatedAtUtc",
            table: "NinlilDnsRewrites",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "TEXT"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsFilterListEntries_ListId_Value",
            table: "NinlilDnsFilterListEntries",
            columns: new[] { "ListId", "Value" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsFilterListEntries_ListId_Value",
            table: "NinlilDnsFilterListEntries"
        );

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            table: "NinlilDnsRewrites",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsFilterListEntries_ListId",
            table: "NinlilDnsFilterListEntries",
            column: "ListId"
        );
    }
}

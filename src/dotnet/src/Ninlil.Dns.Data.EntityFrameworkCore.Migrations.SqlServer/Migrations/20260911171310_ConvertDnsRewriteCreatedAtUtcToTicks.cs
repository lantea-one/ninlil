using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
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

            // SQL Server refuses to convert datetimeoffset to bigint at all -- a plain AlterColumn
            // fails with "Operand type clash" even against an empty table -- so the ticks value is
            // written into a new column, the old one dropped, and the new one renamed into place.
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUtcTicks",
                table: "NinlilDnsRewrites",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE [NinlilDnsRewrites] SET [CreatedAtUtcTicks] = DATEDIFF_BIG(MICROSECOND, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset), [CreatedAtUtc]) * 10 + 621355968000000000;
                """
            );

            migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "NinlilDnsRewrites");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtcTicks",
                table: "NinlilDnsRewrites",
                newName: "CreatedAtUtc"
            );

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUtc",
                table: "NinlilDnsRewrites",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true
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
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsFilterListEntries_ListId",
                table: "NinlilDnsFilterListEntries",
                column: "ListId"
            );
        }
    }
}

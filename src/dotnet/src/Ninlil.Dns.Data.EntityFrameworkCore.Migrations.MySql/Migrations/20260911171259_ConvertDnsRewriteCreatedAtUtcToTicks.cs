using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
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

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUtc",
                table: "NinlilDnsRewrites",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetime(6)"
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
                type: "datetime(6)",
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

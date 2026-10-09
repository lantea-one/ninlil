using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

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

        // See ConvertDnssecKeyCreatedAtUtcToTicks's own Up() doc comment for why a plain
        // AlterColumn call here fails against a real PostgreSQL database (no implicit
        // timestamptz-to-bigint cast exists) and for what this USING expression computes.
        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDnsRewrites"
            ALTER COLUMN "CreatedAtUtc" TYPE bigint
            USING ((EXTRACT(EPOCH FROM "CreatedAtUtc") * 1000000)::bigint * 10 + 621355968000000000);
            """
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

        // See ConvertDnssecKeyCreatedAtUtcToTicks's own Down() doc comment for why a plain
        // AlterColumn call here fails against a real PostgreSQL database and for what this
        // USING expression computes.
        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDnsRewrites"
            ALTER COLUMN "CreatedAtUtc" TYPE timestamp with time zone
            USING (to_timestamp((("CreatedAtUtc" - 621355968000000000) / 10)::double precision / 1000000.0));
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsFilterListEntries_ListId",
            table: "NinlilDnsFilterListEntries",
            column: "ListId"
        );
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class ConvertDhcpLeaseTimesToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Raw SQL runs before the AlterColumn calls: EF defers SQLite's table rebuild past
        // any raw SQL, so the text is converted to UTC ticks (millisecond precision, via
        // julianday) while the columns are still TEXT. Null expiry stays null.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDhcpLeases" SET "LeaseObtainedAtUtc" = CAST(ROUND((julianday("LeaseObtainedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000 WHERE "LeaseObtainedAtUtc" IS NOT NULL;
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE "NinlilDhcpLeases" SET "LeaseExpiresAtUtc" = CAST(ROUND((julianday("LeaseExpiresAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000 WHERE "LeaseExpiresAtUtc" IS NOT NULL;
            """
        );

        // Repairs rows the 2026-09-11 ticks migrations left as ISO-8601 text in an INTEGER
        // column on SQLite. Only text values match, so correct ticks rows are untouched.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDnsDnssecKeys" SET "CreatedAtUtc" = CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000 WHERE typeof("CreatedAtUtc") = 'text';
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE "NinlilDnsRewrites" SET "CreatedAtUtc" = CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000 WHERE typeof("CreatedAtUtc") = 'text';
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE "NinlilDnsClientGroupListAssignments" SET "CreatedAtUtc" = CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000 WHERE typeof("CreatedAtUtc") = 'text';
            """
        );

        migrationBuilder.AlterColumn<long>(
            name: "LeaseObtainedAtUtc",
            table: "NinlilDhcpLeases",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<long>(
            name: "LeaseExpiresAtUtc",
            table: "NinlilDhcpLeases",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "TEXT",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Ticks back to ISO-8601 text before the columns are rebuilt as TEXT. The 2026-09-11
        // repair is not undone.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDhcpLeases" SET "LeaseObtainedAtUtc" = strftime('%Y-%m-%d %H:%M:%f', ("LeaseObtainedAtUtc" - 621355968000000000) / 10000000.0, 'unixepoch') || '+00:00' WHERE "LeaseObtainedAtUtc" IS NOT NULL;
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE "NinlilDhcpLeases" SET "LeaseExpiresAtUtc" = strftime('%Y-%m-%d %H:%M:%f', ("LeaseExpiresAtUtc" - 621355968000000000) / 10000000.0, 'unixepoch') || '+00:00' WHERE "LeaseExpiresAtUtc" IS NOT NULL;
            """
        );

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "LeaseObtainedAtUtc",
            table: "NinlilDhcpLeases",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(long),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "LeaseExpiresAtUtc",
            table: "NinlilDhcpLeases",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(long),
            oldType: "INTEGER",
            oldNullable: true);
    }
}

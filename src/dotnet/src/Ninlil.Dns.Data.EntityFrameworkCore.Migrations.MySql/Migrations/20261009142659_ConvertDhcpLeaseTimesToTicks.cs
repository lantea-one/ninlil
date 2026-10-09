using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class ConvertDhcpLeaseTimesToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // MySQL's own datetime-to-bigint ALTER yields a YYYYMMDDhhmmss number rather than
        // ticks, so the ticks go into a new column that replaces the old one.
        migrationBuilder.AddColumn<long>(
            name: "LeaseObtainedAtUtcTicks",
            table: "NinlilDhcpLeases",
            type: "bigint",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpLeases` SET `LeaseObtainedAtUtcTicks` = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', `LeaseObtainedAtUtc`) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "LeaseObtainedAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseObtainedAtUtcTicks",
            table: "NinlilDhcpLeases",
            newName: "LeaseObtainedAtUtc"
        );

        migrationBuilder.AddColumn<long>(
            name: "LeaseExpiresAtUtcTicks",
            table: "NinlilDhcpLeases",
            type: "bigint",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpLeases` SET `LeaseExpiresAtUtcTicks` = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', `LeaseExpiresAtUtc`) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "LeaseExpiresAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseExpiresAtUtcTicks",
            table: "NinlilDhcpLeases",
            newName: "LeaseExpiresAtUtc"
        );

        // Repairs rows the 2026-09-11 ticks migrations left as YYYYMMDDhhmmss numbers on
        // MySQL. A real ticks value is above 6e17, so only the 14-digit values match.
        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsDnssecKeys` SET `CreatedAtUtc` = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', STR_TO_DATE(CAST(`CreatedAtUtc` AS CHAR), '%Y%m%d%H%i%s')) * 10 + 621355968000000000 WHERE `CreatedAtUtc` BETWEEN 10000101000000 AND 99991231235959;
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsRewrites` SET `CreatedAtUtc` = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', STR_TO_DATE(CAST(`CreatedAtUtc` AS CHAR), '%Y%m%d%H%i%s')) * 10 + 621355968000000000 WHERE `CreatedAtUtc` BETWEEN 10000101000000 AND 99991231235959;
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsClientGroupListAssignments` SET `CreatedAtUtc` = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', STR_TO_DATE(CAST(`CreatedAtUtc` AS CHAR), '%Y%m%d%H%i%s')) * 10 + 621355968000000000 WHERE `CreatedAtUtc` BETWEEN 10000101000000 AND 99991231235959;
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The 2026-09-11 repair is not undone.
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LeaseObtainedAtUtcValue",
            table: "NinlilDhcpLeases",
            type: "datetime(6)",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpLeases` SET `LeaseObtainedAtUtcValue` = DATE_ADD('1970-01-01 00:00:00.000000', INTERVAL (`LeaseObtainedAtUtc` - 621355968000000000) DIV 10 MICROSECOND);
            """
        );

        migrationBuilder.DropColumn(name: "LeaseObtainedAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseObtainedAtUtcValue",
            table: "NinlilDhcpLeases",
            newName: "LeaseObtainedAtUtc"
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LeaseExpiresAtUtcValue",
            table: "NinlilDhcpLeases",
            type: "datetime(6)",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpLeases` SET `LeaseExpiresAtUtcValue` = DATE_ADD('1970-01-01 00:00:00.000000', INTERVAL (`LeaseExpiresAtUtc` - 621355968000000000) DIV 10 MICROSECOND);
            """
        );

        migrationBuilder.DropColumn(name: "LeaseExpiresAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseExpiresAtUtcValue",
            table: "NinlilDhcpLeases",
            newName: "LeaseExpiresAtUtc"
        );
    }
}

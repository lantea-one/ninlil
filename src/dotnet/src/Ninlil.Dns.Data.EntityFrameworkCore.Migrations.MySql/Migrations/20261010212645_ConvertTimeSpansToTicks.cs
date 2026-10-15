using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class ConvertTimeSpansToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A time(6) column holds up to 838:59:59 and whole microseconds, so the ticks (100 ns)
        // go into a new bigint column that replaces the old one. TIME_TO_SEC is signed, and
        // MICROSECOND is not, so the sign is applied to the fraction.
        migrationBuilder.AddColumn<long>(
            name: "CloudSyncIntervalTicks",
            table: "NinlilDnsZones",
            type: "bigint",
            nullable: false,
            defaultValue: 3000000000L
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsZones` SET `CloudSyncIntervalTicks` = (TIME_TO_SEC(`CloudSyncInterval`) * 1000000 + IF(`CloudSyncInterval` < '00:00:00', -1, 1) * MICROSECOND(`CloudSyncInterval`)) * 10;
            """
        );

        migrationBuilder.DropColumn(name: "CloudSyncInterval", table: "NinlilDnsZones");

        migrationBuilder.RenameColumn(name: "CloudSyncIntervalTicks", table: "NinlilDnsZones", newName: "CloudSyncInterval");

        migrationBuilder.AddColumn<long>(
            name: "RefreshIntervalTicks",
            table: "NinlilDnsFilterLists",
            type: "bigint",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsFilterLists` SET `RefreshIntervalTicks` = (TIME_TO_SEC(`RefreshInterval`) * 1000000 + IF(`RefreshInterval` < '00:00:00', -1, 1) * MICROSECOND(`RefreshInterval`)) * 10;
            """
        );

        migrationBuilder.DropColumn(name: "RefreshInterval", table: "NinlilDnsFilterLists");

        migrationBuilder.RenameColumn(name: "RefreshIntervalTicks", table: "NinlilDnsFilterLists", newName: "RefreshInterval");

        migrationBuilder.AddColumn<long>(
            name: "MaxLeaseDurationTicks",
            table: "NinlilDhcpScopes",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpScopes` SET `MaxLeaseDurationTicks` = (TIME_TO_SEC(`MaxLeaseDuration`) * 1000000 + IF(`MaxLeaseDuration` < '00:00:00', -1, 1) * MICROSECOND(`MaxLeaseDuration`)) * 10;
            """
        );

        migrationBuilder.DropColumn(name: "MaxLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "MaxLeaseDurationTicks", table: "NinlilDhcpScopes", newName: "MaxLeaseDuration");

        migrationBuilder.AddColumn<long>(
            name: "DefaultLeaseDurationTicks",
            table: "NinlilDhcpScopes",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpScopes` SET `DefaultLeaseDurationTicks` = (TIME_TO_SEC(`DefaultLeaseDuration`) * 1000000 + IF(`DefaultLeaseDuration` < '00:00:00', -1, 1) * MICROSECOND(`DefaultLeaseDuration`)) * 10;
            """
        );

        migrationBuilder.DropColumn(name: "DefaultLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "DefaultLeaseDurationTicks", table: "NinlilDhcpScopes", newName: "DefaultLeaseDuration");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A time(6) column holds 838:59:59 at most and whole microseconds, so longer
        // spans are clamped to that value, negative ones to zero, and a tick below one
        // microsecond is cut off. The multiplication by 0.000001 keeps the microseconds exact
        // (a division would round to four decimals).
        migrationBuilder.AddColumn<TimeSpan>(
            name: "CloudSyncIntervalTime",
            table: "NinlilDnsZones",
            type: "time(6)",
            nullable: false,
            defaultValue: new TimeSpan(0, 0, 5, 0, 0)
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsZones` SET `CloudSyncIntervalTime` = SEC_TO_TIME((LEAST(GREATEST(`CloudSyncInterval`, 0), 30203990000000) DIV 10) * 0.000001) WHERE `CloudSyncInterval` IS NOT NULL;
            """
        );

        migrationBuilder.DropColumn(name: "CloudSyncInterval", table: "NinlilDnsZones");

        migrationBuilder.RenameColumn(name: "CloudSyncIntervalTime", table: "NinlilDnsZones", newName: "CloudSyncInterval");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "RefreshIntervalTime",
            table: "NinlilDnsFilterLists",
            type: "time(6)",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDnsFilterLists` SET `RefreshIntervalTime` = SEC_TO_TIME((LEAST(GREATEST(`RefreshInterval`, 0), 30203990000000) DIV 10) * 0.000001) WHERE `RefreshInterval` IS NOT NULL;
            """
        );

        migrationBuilder.DropColumn(name: "RefreshInterval", table: "NinlilDnsFilterLists");

        migrationBuilder.RenameColumn(name: "RefreshIntervalTime", table: "NinlilDnsFilterLists", newName: "RefreshInterval");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "MaxLeaseDurationTime",
            table: "NinlilDhcpScopes",
            type: "time(6)",
            nullable: false,
            defaultValue: TimeSpan.Zero
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpScopes` SET `MaxLeaseDurationTime` = SEC_TO_TIME((LEAST(GREATEST(`MaxLeaseDuration`, 0), 30203990000000) DIV 10) * 0.000001) WHERE `MaxLeaseDuration` IS NOT NULL;
            """
        );

        migrationBuilder.DropColumn(name: "MaxLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "MaxLeaseDurationTime", table: "NinlilDhcpScopes", newName: "MaxLeaseDuration");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "DefaultLeaseDurationTime",
            table: "NinlilDhcpScopes",
            type: "time(6)",
            nullable: false,
            defaultValue: TimeSpan.Zero
        );

        migrationBuilder.Sql(
            """
            UPDATE `NinlilDhcpScopes` SET `DefaultLeaseDurationTime` = SEC_TO_TIME((LEAST(GREATEST(`DefaultLeaseDuration`, 0), 30203990000000) DIV 10) * 0.000001) WHERE `DefaultLeaseDuration` IS NOT NULL;
            """
        );

        migrationBuilder.DropColumn(name: "DefaultLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "DefaultLeaseDurationTime", table: "NinlilDhcpScopes", newName: "DefaultLeaseDuration");
    }
}

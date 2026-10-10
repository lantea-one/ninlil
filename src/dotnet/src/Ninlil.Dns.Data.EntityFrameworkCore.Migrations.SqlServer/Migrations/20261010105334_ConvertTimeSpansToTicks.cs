using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class ConvertTimeSpansToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQL Server cannot alter time to bigint, so the ticks go into a new column that replaces
        // the old one. time(7) counts 100 ns units, the same unit as a tick.
        migrationBuilder.AddColumn<long>(
            name: "CloudSyncIntervalTicks",
            table: "NinlilDnsZones",
            type: "bigint",
            nullable: false,
            defaultValue: 3000000000L
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsZones] SET [CloudSyncIntervalTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time(7)), [CloudSyncInterval]) / 100;
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDnsZones]') AND c.[name] = N'CloudSyncInterval');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDnsZones] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
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
            UPDATE [NinlilDnsFilterLists] SET [RefreshIntervalTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time(7)), [RefreshInterval]) / 100;
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDnsFilterLists]') AND c.[name] = N'RefreshInterval');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDnsFilterLists] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
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
            UPDATE [NinlilDhcpScopes] SET [MaxLeaseDurationTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time(7)), [MaxLeaseDuration]) / 100;
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDhcpScopes]') AND c.[name] = N'MaxLeaseDuration');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDhcpScopes] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
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
            UPDATE [NinlilDhcpScopes] SET [DefaultLeaseDurationTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time(7)), [DefaultLeaseDuration]) / 100;
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDhcpScopes]') AND c.[name] = N'DefaultLeaseDuration');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDhcpScopes] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
            """
        );

        migrationBuilder.DropColumn(name: "DefaultLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "DefaultLeaseDurationTicks", table: "NinlilDhcpScopes", newName: "DefaultLeaseDuration");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A time column holds less than 24 hours, so longer spans are clamped to 23:59:59.9999999
        // and negative ones to zero.
        migrationBuilder.AddColumn<TimeSpan>(
            name: "CloudSyncIntervalTime",
            table: "NinlilDnsZones",
            type: "time",
            nullable: false,
            defaultValue: new TimeSpan(0, 0, 5, 0, 0)
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsZones] SET [CloudSyncIntervalTime] = TIMEFROMPARTS(CAST(CASE WHEN [CloudSyncInterval] < 0 THEN 0 WHEN [CloudSyncInterval] > 863999999999 THEN 863999999999 ELSE [CloudSyncInterval] END / 36000000000 AS int), CAST(CASE WHEN [CloudSyncInterval] < 0 THEN 0 WHEN [CloudSyncInterval] > 863999999999 THEN 863999999999 ELSE [CloudSyncInterval] END / 600000000 % 60 AS int), CAST(CASE WHEN [CloudSyncInterval] < 0 THEN 0 WHEN [CloudSyncInterval] > 863999999999 THEN 863999999999 ELSE [CloudSyncInterval] END / 10000000 % 60 AS int), CAST(CASE WHEN [CloudSyncInterval] < 0 THEN 0 WHEN [CloudSyncInterval] > 863999999999 THEN 863999999999 ELSE [CloudSyncInterval] END % 10000000 AS int), 7);
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDnsZones]') AND c.[name] = N'CloudSyncInterval');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDnsZones] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
            """
        );

        migrationBuilder.DropColumn(name: "CloudSyncInterval", table: "NinlilDnsZones");

        migrationBuilder.RenameColumn(name: "CloudSyncIntervalTime", table: "NinlilDnsZones", newName: "CloudSyncInterval");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "RefreshIntervalTime",
            table: "NinlilDnsFilterLists",
            type: "time",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsFilterLists] SET [RefreshIntervalTime] = CASE WHEN [RefreshInterval] IS NULL THEN NULL ELSE TIMEFROMPARTS(CAST(CASE WHEN [RefreshInterval] < 0 THEN 0 WHEN [RefreshInterval] > 863999999999 THEN 863999999999 ELSE [RefreshInterval] END / 36000000000 AS int), CAST(CASE WHEN [RefreshInterval] < 0 THEN 0 WHEN [RefreshInterval] > 863999999999 THEN 863999999999 ELSE [RefreshInterval] END / 600000000 % 60 AS int), CAST(CASE WHEN [RefreshInterval] < 0 THEN 0 WHEN [RefreshInterval] > 863999999999 THEN 863999999999 ELSE [RefreshInterval] END / 10000000 % 60 AS int), CAST(CASE WHEN [RefreshInterval] < 0 THEN 0 WHEN [RefreshInterval] > 863999999999 THEN 863999999999 ELSE [RefreshInterval] END % 10000000 AS int), 7) END;
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDnsFilterLists]') AND c.[name] = N'RefreshInterval');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDnsFilterLists] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
            """
        );

        migrationBuilder.DropColumn(name: "RefreshInterval", table: "NinlilDnsFilterLists");

        migrationBuilder.RenameColumn(name: "RefreshIntervalTime", table: "NinlilDnsFilterLists", newName: "RefreshInterval");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "MaxLeaseDurationTime",
            table: "NinlilDhcpScopes",
            type: "time",
            nullable: false,
            defaultValue: TimeSpan.Zero
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDhcpScopes] SET [MaxLeaseDurationTime] = TIMEFROMPARTS(CAST(CASE WHEN [MaxLeaseDuration] < 0 THEN 0 WHEN [MaxLeaseDuration] > 863999999999 THEN 863999999999 ELSE [MaxLeaseDuration] END / 36000000000 AS int), CAST(CASE WHEN [MaxLeaseDuration] < 0 THEN 0 WHEN [MaxLeaseDuration] > 863999999999 THEN 863999999999 ELSE [MaxLeaseDuration] END / 600000000 % 60 AS int), CAST(CASE WHEN [MaxLeaseDuration] < 0 THEN 0 WHEN [MaxLeaseDuration] > 863999999999 THEN 863999999999 ELSE [MaxLeaseDuration] END / 10000000 % 60 AS int), CAST(CASE WHEN [MaxLeaseDuration] < 0 THEN 0 WHEN [MaxLeaseDuration] > 863999999999 THEN 863999999999 ELSE [MaxLeaseDuration] END % 10000000 AS int), 7);
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDhcpScopes]') AND c.[name] = N'MaxLeaseDuration');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDhcpScopes] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
            """
        );

        migrationBuilder.DropColumn(name: "MaxLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "MaxLeaseDurationTime", table: "NinlilDhcpScopes", newName: "MaxLeaseDuration");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "DefaultLeaseDurationTime",
            table: "NinlilDhcpScopes",
            type: "time",
            nullable: false,
            defaultValue: TimeSpan.Zero
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDhcpScopes] SET [DefaultLeaseDurationTime] = TIMEFROMPARTS(CAST(CASE WHEN [DefaultLeaseDuration] < 0 THEN 0 WHEN [DefaultLeaseDuration] > 863999999999 THEN 863999999999 ELSE [DefaultLeaseDuration] END / 36000000000 AS int), CAST(CASE WHEN [DefaultLeaseDuration] < 0 THEN 0 WHEN [DefaultLeaseDuration] > 863999999999 THEN 863999999999 ELSE [DefaultLeaseDuration] END / 600000000 % 60 AS int), CAST(CASE WHEN [DefaultLeaseDuration] < 0 THEN 0 WHEN [DefaultLeaseDuration] > 863999999999 THEN 863999999999 ELSE [DefaultLeaseDuration] END / 10000000 % 60 AS int), CAST(CASE WHEN [DefaultLeaseDuration] < 0 THEN 0 WHEN [DefaultLeaseDuration] > 863999999999 THEN 863999999999 ELSE [DefaultLeaseDuration] END % 10000000 AS int), 7);
            """
        );

        migrationBuilder.Sql(
            """
            DECLARE @constraint nvarchar(128) = (SELECT d.[name] FROM sys.default_constraints d JOIN sys.columns c ON c.[object_id] = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.parent_object_id = OBJECT_ID(N'[NinlilDhcpScopes]') AND c.[name] = N'DefaultLeaseDuration');
            IF @constraint IS NOT NULL BEGIN DECLARE @drop nvarchar(400) = N'ALTER TABLE [NinlilDhcpScopes] DROP CONSTRAINT ' + QUOTENAME(@constraint); EXEC(@drop); END
            """
        );

        migrationBuilder.DropColumn(name: "DefaultLeaseDuration", table: "NinlilDhcpScopes");

        migrationBuilder.RenameColumn(name: "DefaultLeaseDurationTime", table: "NinlilDhcpScopes", newName: "DefaultLeaseDuration");
    }
}

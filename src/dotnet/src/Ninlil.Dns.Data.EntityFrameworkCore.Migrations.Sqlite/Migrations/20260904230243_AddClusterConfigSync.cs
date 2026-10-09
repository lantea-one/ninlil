using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddClusterConfigSync : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsZones",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsWebhookSubscriptions",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsUsers",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsTsigKeys",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsRoles",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsRewrites",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsRecords",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsLeases",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsForwarders",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsFilterLists",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsFilterListEntries",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAtUtc",
            table: "NinlilDnsFilterListEntries",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsDnssecKeys",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsClusterNodes",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsCloudDnsProviderCredentials",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsClientGroups",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsAccessControlRules",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsClusterSyncedSettingsStates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SectionName = table.Column<string>(
                    type: "TEXT",
                    maxLength: 255,
                    nullable: false
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterSyncedSettingsStates", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsClusterSyncedSettingsStates_SectionName",
            table: "NinlilDnsClusterSyncedSettingsStates",
            column: "SectionName",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsClusterSyncedSettingsStates");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsZones");

        migrationBuilder.DropColumn(
            name: "OriginatingNodeId",
            table: "NinlilDnsWebhookSubscriptions"
        );

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsUsers");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsTsigKeys");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsRoles");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsRewrites");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsRecords");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsLeases");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsForwarders");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsFilterLists");

        migrationBuilder.DropColumn(
            name: "OriginatingNodeId",
            table: "NinlilDnsFilterListEntries"
        );

        migrationBuilder.DropColumn(name: "UpdatedAtUtc", table: "NinlilDnsFilterListEntries");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsDnssecKeys");

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsClusterNodes");

        migrationBuilder.DropColumn(
            name: "OriginatingNodeId",
            table: "NinlilDnsCloudDnsProviderCredentials"
        );

        migrationBuilder.DropColumn(name: "OriginatingNodeId", table: "NinlilDnsClientGroups");

        migrationBuilder.DropColumn(
            name: "OriginatingNodeId",
            table: "NinlilDnsAccessControlRules"
        );
    }
}

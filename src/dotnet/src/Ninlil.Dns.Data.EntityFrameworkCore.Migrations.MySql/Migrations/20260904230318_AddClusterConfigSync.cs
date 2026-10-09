using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddClusterConfigSync : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsZones",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsWebhookSubscriptions",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsUsers",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsTsigKeys",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsRoles",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsRewrites",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsRecords",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsLeases",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsForwarders",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsFilterLists",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsFilterListEntries",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAtUtc",
            table: "NinlilDnsFilterListEntries",
            type: "datetime(6)",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsDnssecKeys",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsClusterNodes",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsCloudDnsProviderCredentials",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsClientGroups",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder.AddColumn<Guid>(
            name: "OriginatingNodeId",
            table: "NinlilDnsAccessControlRules",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsClusterSyncedSettingsStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    SectionName = table
                        .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClusterSyncedSettingsStates", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

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

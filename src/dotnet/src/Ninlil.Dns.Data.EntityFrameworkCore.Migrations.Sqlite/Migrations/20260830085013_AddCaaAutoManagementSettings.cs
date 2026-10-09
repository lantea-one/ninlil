using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddCaaAutoManagementSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AutoManageCaaRecords",
            table: "NinlilDnsZones",
            type: "INTEGER",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "CaaIssuerDomain",
            table: "NinlilDnsZones",
            type: "TEXT",
            maxLength: 255,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "ManagedByFeature",
            table: "NinlilDnsRecords",
            type: "TEXT",
            maxLength: 32,
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AutoManageCaaRecords", table: "NinlilDnsZones");

        migrationBuilder.DropColumn(name: "CaaIssuerDomain", table: "NinlilDnsZones");

        migrationBuilder.DropColumn(name: "ManagedByFeature", table: "NinlilDnsRecords");
    }
}

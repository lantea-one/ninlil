using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddZoneCatalogMembership : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "MemberOfCatalogZoneId",
            table: "NinlilDnsZones",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MemberOfCatalogZoneId", table: "NinlilDnsZones");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddZoneCatalogMembership : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "MemberOfCatalogZoneId",
            table: "NinlilDnsZones",
            type: "uniqueidentifier",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MemberOfCatalogZoneId", table: "NinlilDnsZones");
    }
}

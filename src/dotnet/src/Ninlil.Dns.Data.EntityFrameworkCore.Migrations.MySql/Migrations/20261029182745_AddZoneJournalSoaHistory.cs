using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddZoneJournalSoaHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NewSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "OldSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NewSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries");

        migrationBuilder.DropColumn(
            name: "OldSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries");
    }
}

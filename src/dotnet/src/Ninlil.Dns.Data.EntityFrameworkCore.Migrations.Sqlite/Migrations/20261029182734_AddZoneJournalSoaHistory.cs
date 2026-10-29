using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddZoneJournalSoaHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NewSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OldSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries",
            type: "TEXT",
            nullable: true);
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

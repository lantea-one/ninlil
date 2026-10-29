using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddZoneJournalSoaHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NewSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OldSoaRecordJson",
            table: "NinlilDnsZoneChangeJournalEntries",
            type: "text",
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

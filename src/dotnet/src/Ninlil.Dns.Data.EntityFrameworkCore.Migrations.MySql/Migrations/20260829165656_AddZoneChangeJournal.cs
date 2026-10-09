using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddZoneChangeJournal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .CreateTable(
                name: "NinlilDnsZoneChangeJournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ZoneId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    OldSerial = table.Column<long>(type: "bigint", nullable: false),
                    NewSerial = table.Column<long>(type: "bigint", nullable: false),
                    RemovedRecordsJson = table
                        .Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AddedRecordsJson = table
                        .Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsZoneChangeJournalEntries", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsZoneChangeJournalEntries_ZoneId_OldSerial",
            table: "NinlilDnsZoneChangeJournalEntries",
            columns: new[] { "ZoneId", "OldSerial" }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsZoneChangeJournalEntries");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneChangeJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NinlilDnsZoneChangeJournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldSerial = table.Column<long>(type: "bigint", nullable: false),
                    NewSerial = table.Column<long>(type: "bigint", nullable: false),
                    RemovedRecordsJson = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: false
                    ),
                    AddedRecordsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsZoneChangeJournalEntries", x => x.Id);
                }
            );

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
}

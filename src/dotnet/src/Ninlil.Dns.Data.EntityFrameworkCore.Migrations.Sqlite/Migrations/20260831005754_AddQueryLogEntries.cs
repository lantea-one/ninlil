using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddQueryLogEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsQueryLogEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                ClientAddress = table.Column<string>(
                    type: "TEXT",
                    maxLength: 45,
                    nullable: true
                ),
                Protocol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                QueriedName = table.Column<string>(
                    type: "TEXT",
                    maxLength: 255,
                    nullable: true
                ),
                QueriedType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                ResponseCode = table.Column<string>(
                    type: "TEXT",
                    maxLength: 32,
                    nullable: false
                ),
                AnswerCount = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsQueryLogEntries", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ClientAddress",
            table: "NinlilDnsQueryLogEntries",
            column: "ClientAddress"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_OccurredAtUtc",
            table: "NinlilDnsQueryLogEntries",
            column: "OccurredAtUtc"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_QueriedName",
            table: "NinlilDnsQueryLogEntries",
            column: "QueriedName"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsQueryLogEntries_ResponseCode",
            table: "NinlilDnsQueryLogEntries",
            column: "ResponseCode"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsQueryLogEntries");
    }
}

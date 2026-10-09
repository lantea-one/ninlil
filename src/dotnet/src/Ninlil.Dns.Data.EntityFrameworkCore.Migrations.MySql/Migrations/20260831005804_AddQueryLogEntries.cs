using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddQueryLogEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .CreateTable(
                name: "NinlilDnsQueryLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    OccurredAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    ClientAddress = table
                        .Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Protocol = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QueriedName = table
                        .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QueriedType = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponseCode = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnswerCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsQueryLogEntries", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDnsClientSchedules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .CreateTable(
                name: "NinlilDnsClientGroupSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ClientGroupId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    DaysOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    TimeZoneId = table
                        .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Comments = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClientGroupSchedules", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsClientSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ClientId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    DaysOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    TimeZoneId = table
                        .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Comments = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: true,
                        collation: "ascii_general_ci"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClientSchedules", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsClientGroupSchedules_ClientGroupId",
            table: "NinlilDnsClientGroupSchedules",
            column: "ClientGroupId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsClientSchedules_ClientId",
            table: "NinlilDnsClientSchedules",
            column: "ClientId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsClientGroupSchedules");

        migrationBuilder.DropTable(name: "NinlilDnsClientSchedules");
    }
}

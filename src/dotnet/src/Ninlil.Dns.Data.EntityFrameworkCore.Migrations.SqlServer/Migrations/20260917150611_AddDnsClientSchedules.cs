using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddDnsClientSchedules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsClientGroupSchedules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DaysOfWeek = table.Column<int>(type: "int", nullable: false),
                StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                TimeZoneId = table.Column<string>(
                    type: "nvarchar(128)",
                    maxLength: 128,
                    nullable: false
                ),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                Comments = table.Column<string>(
                    type: "nvarchar(max)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClientGroupSchedules", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsClientSchedules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DaysOfWeek = table.Column<int>(type: "int", nullable: false),
                StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                TimeZoneId = table.Column<string>(
                    type: "nvarchar(128)",
                    maxLength: 128,
                    nullable: false
                ),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                Comments = table.Column<string>(
                    type: "nvarchar(max)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClientSchedules", x => x.Id);
            }
        );

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

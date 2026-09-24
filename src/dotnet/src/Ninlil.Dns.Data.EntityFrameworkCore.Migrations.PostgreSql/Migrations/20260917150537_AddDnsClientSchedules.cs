using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations
{
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    EndTime = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    TimeZoneId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    Comments = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    EndTime = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: false
                    ),
                    TimeZoneId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    Comments = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
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
}

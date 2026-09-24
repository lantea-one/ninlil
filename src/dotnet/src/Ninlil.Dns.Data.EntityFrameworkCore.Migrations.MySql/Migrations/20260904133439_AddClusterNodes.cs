using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddClusterNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "NinlilDnsClusterNodes",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        RemoteNodeId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Host = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Port = table.Column<int>(type: "int", nullable: false),
                        TsigKeyName = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        TlsCertificateThumbprint = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Status = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        LastHeartbeatAtUtc = table.Column<DateTimeOffset>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        MissedHeartbeatCount = table.Column<int>(type: "int", nullable: false),
                        Comments = table
                            .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        CreatedAtUtc = table.Column<DateTimeOffset>(
                            type: "datetime(6)",
                            nullable: false
                        ),
                        UpdatedAtUtc = table.Column<DateTimeOffset>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_NinlilDnsClusterNodes", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClusterNodes_RemoteNodeId",
                table: "NinlilDnsClusterNodes",
                column: "RemoteNodeId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsClusterNodes");
        }
    }
}

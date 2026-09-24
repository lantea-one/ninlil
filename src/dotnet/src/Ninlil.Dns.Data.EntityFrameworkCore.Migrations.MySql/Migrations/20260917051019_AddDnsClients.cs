using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "NinlilDnsClients",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        ClientIdentifier = table
                            .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ProtocolVersion = table
                            .Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        DisplayName = table
                            .Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ClientGroupId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: true,
                            collation: "ascii_general_ci"
                        ),
                        IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                        OriginatingNodeId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: true,
                            collation: "ascii_general_ci"
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_NinlilDnsClients", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDhcpLeases_IpAddress",
                table: "NinlilDhcpLeases",
                column: "IpAddress"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClients_ClientGroupId",
                table: "NinlilDnsClients",
                column: "ClientGroupId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClients_ClientIdentifier_ProtocolVersion",
                table: "NinlilDnsClients",
                columns: new[] { "ClientIdentifier", "ProtocolVersion" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsClients");

            migrationBuilder.DropIndex(
                name: "IX_NinlilDhcpLeases_IpAddress",
                table: "NinlilDhcpLeases"
            );
        }
    }
}

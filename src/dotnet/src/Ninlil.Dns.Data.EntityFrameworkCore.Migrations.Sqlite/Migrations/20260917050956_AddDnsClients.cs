using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NinlilDnsClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientIdentifier = table.Column<string>(
                        type: "TEXT",
                        maxLength: 256,
                        nullable: false
                    ),
                    ProtocolVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 256,
                        nullable: true
                    ),
                    ClientGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClients", x => x.Id);
                }
            );

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

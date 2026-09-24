using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsClientListAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "NinlilDnsClientListAssignments",
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
                        ListId = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_NinlilDnsClientListAssignments", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientListAssignments_ClientId",
                table: "NinlilDnsClientListAssignments",
                column: "ClientId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientListAssignments_ListId",
                table: "NinlilDnsClientListAssignments",
                column: "ListId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsClientListAssignments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddDnsClientListAssignments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsClientListAssignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                ListId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClientListAssignments", x => x.Id);
            }
        );

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

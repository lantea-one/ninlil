using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddClusterPeerConfigBackfill : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsClusterPeerConfigBackfills",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                ClusterNodeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                LastError = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterPeerConfigBackfills", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsClusterPeerConfigBackfills_NinlilDnsClusterNodes_Cl~",
                    column: x => x.ClusterNodeId,
                    principalTable: "NinlilDnsClusterNodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsClusterPeerConfigBackfills_ClusterNodeId",
            table: "NinlilDnsClusterPeerConfigBackfills",
            column: "ClusterNodeId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NinlilDnsClusterPeerConfigBackfills");
    }
}

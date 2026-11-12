using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

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
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ClusterNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterPeerConfigBackfills", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsClusterPeerConfigBackfills_NinlilDnsClusterNodes_ClusterNodeId",
                    column: x => x.ClusterNodeId,
                    principalTable: "NinlilDnsClusterNodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

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
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClusterNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterPeerConfigBackfills", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsClusterPeerConfigBackfills_NinlilDnsClusterNodes_C~",
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

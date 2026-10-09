using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddClusterNodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsClusterNodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                RemoteNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Host = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                Port = table.Column<int>(type: "int", nullable: false),
                TsigKeyName = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                TlsCertificateThumbprint = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false
                ),
                Status = table.Column<string>(
                    type: "nvarchar(32)",
                    maxLength: 32,
                    nullable: false
                ),
                LastHeartbeatAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
                MissedHeartbeatCount = table.Column<int>(type: "int", nullable: false),
                Comments = table.Column<string>(
                    type: "nvarchar(max)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterNodes", x => x.Id);
            }
        );

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

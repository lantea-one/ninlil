using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

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
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientIdentifier = table.Column<string>(
                    type: "nvarchar(256)",
                    maxLength: 256,
                    nullable: false
                ),
                ProtocolVersion = table.Column<string>(
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false
                ),
                DisplayName = table.Column<string>(
                    type: "nvarchar(256)",
                    maxLength: 256,
                    nullable: true
                ),
                ClientGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
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
                OriginatingNodeId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true
                ),
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

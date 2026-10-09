using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddClusterSyncTombstones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsClusterSyncTombstones",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                EntityKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterSyncTombstones", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsClusterSyncTombstones_EntityType_EntityKey",
            table: "NinlilDnsClusterSyncTombstones",
            columns: new[] { "EntityType", "EntityKey" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NinlilDnsClusterSyncTombstones");
    }
}

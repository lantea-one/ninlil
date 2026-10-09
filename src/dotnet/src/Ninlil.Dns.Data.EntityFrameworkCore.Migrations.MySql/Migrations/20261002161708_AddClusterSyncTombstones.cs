using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

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
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                EntityType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                EntityKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClusterSyncTombstones", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

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

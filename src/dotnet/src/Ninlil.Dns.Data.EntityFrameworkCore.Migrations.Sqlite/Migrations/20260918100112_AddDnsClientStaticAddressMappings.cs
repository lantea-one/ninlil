using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDnsClientStaticAddressMappings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsClientStaticAddressMappings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Cidr = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
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
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsClientStaticAddressMappings", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsClientStaticAddressMappings_IsDisabled",
            table: "NinlilDnsClientStaticAddressMappings",
            column: "IsDisabled"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsClientStaticAddressMappings");
    }
}

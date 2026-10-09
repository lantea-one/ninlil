using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

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
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Cidr = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false
                ),
                ClientIdentifier = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                ProtocolVersion = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false
                ),
                IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                Comments = table.Column<string>(
                    type: "character varying(4096)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
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

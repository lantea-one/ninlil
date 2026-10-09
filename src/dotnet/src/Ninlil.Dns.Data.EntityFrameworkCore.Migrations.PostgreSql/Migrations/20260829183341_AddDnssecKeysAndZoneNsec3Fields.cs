using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddDnssecKeysAndZoneNsec3Fields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DenialOfExistenceMode",
            table: "NinlilDnsZones",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "Nsec3Iterations",
            table: "NinlilDnsZones",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<string>(
            name: "Nsec3SaltHex",
            table: "NinlilDnsZones",
            type: "text",
            nullable: true
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsDnssecKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                Algorithm = table.Column<byte>(type: "smallint", nullable: false),
                PublicKeyWireFormat = table.Column<byte[]>(type: "bytea", nullable: false),
                PrivateKeyPkcs8Encrypted = table.Column<string>(type: "text", nullable: false),
                KeyTag = table.Column<int>(type: "integer", nullable: false),
                IsRetired = table.Column<bool>(type: "boolean", nullable: false),
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
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsDnssecKeys", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsDnssecKeys_ZoneId",
            table: "NinlilDnsDnssecKeys",
            column: "ZoneId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsDnssecKeys");

        migrationBuilder.DropColumn(name: "DenialOfExistenceMode", table: "NinlilDnsZones");

        migrationBuilder.DropColumn(name: "Nsec3Iterations", table: "NinlilDnsZones");

        migrationBuilder.DropColumn(name: "Nsec3SaltHex", table: "NinlilDnsZones");
    }
}

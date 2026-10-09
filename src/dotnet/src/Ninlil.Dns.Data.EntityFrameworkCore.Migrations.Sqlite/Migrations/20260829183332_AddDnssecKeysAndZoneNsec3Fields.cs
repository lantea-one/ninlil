using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddDnssecKeysAndZoneNsec3Fields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DenialOfExistenceMode",
            table: "NinlilDnsZones",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<ushort>(
            name: "Nsec3Iterations",
            table: "NinlilDnsZones",
            type: "INTEGER",
            nullable: false,
            defaultValue: (ushort)0
        );

        migrationBuilder.AddColumn<string>(
            name: "Nsec3SaltHex",
            table: "NinlilDnsZones",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsDnssecKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                Role = table.Column<int>(type: "INTEGER", nullable: false),
                Algorithm = table.Column<byte>(type: "INTEGER", nullable: false),
                PublicKeyWireFormat = table.Column<byte[]>(type: "BLOB", nullable: false),
                PrivateKeyPkcs8Encrypted = table.Column<string>(type: "TEXT", nullable: false),
                KeyTag = table.Column<ushort>(type: "INTEGER", nullable: false),
                IsRetired = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
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

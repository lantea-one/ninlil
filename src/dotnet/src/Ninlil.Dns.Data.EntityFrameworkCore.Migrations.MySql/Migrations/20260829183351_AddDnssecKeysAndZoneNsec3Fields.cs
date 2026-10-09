using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddDnssecKeysAndZoneNsec3Fields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DenialOfExistenceMode",
            table: "NinlilDnsZones",
            type: "int",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<ushort>(
            name: "Nsec3Iterations",
            table: "NinlilDnsZones",
            type: "smallint unsigned",
            nullable: false,
            defaultValue: (ushort)0
        );

        migrationBuilder
            .AddColumn<string>(
                name: "Nsec3SaltHex",
                table: "NinlilDnsZones",
                type: "longtext",
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsDnssecKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    ZoneId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Algorithm = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    PublicKeyWireFormat = table.Column<byte[]>(
                        type: "longblob",
                        nullable: false
                    ),
                    PrivateKeyPkcs8Encrypted = table
                        .Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyTag = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    IsRetired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Comments = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsDnssecKeys", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

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

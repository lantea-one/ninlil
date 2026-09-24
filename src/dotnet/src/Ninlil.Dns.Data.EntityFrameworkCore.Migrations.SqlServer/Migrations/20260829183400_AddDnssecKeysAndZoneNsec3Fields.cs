using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
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

            migrationBuilder.AddColumn<int>(
                name: "Nsec3Iterations",
                table: "NinlilDnsZones",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "Nsec3SaltHex",
                table: "NinlilDnsZones",
                type: "nvarchar(max)",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsDnssecKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Algorithm = table.Column<byte>(type: "tinyint", nullable: false),
                    PublicKeyWireFormat = table.Column<byte[]>(
                        type: "varbinary(max)",
                        nullable: false
                    ),
                    PrivateKeyPkcs8Encrypted = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: false
                    ),
                    KeyTag = table.Column<int>(type: "int", nullable: false),
                    IsRetired = table.Column<bool>(type: "bit", nullable: false),
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
}

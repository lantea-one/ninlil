using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsClientAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the new per-identifier address table first, so every existing device's own
            // identifier can be copied into it before the old columns carrying it are dropped --
            // an upgraded installation must keep every known client's identity intact.
            migrationBuilder.CreateTable(
                name: "NinlilDnsClientAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    Address = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    FirstSeenUtc = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenUtc = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OriginatingNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsClientAddresses", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientAddresses_ClientId",
                table: "NinlilDnsClientAddresses",
                column: "ClientId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClientAddresses_ClientIdentifier_ProtocolVersion",
                table: "NinlilDnsClientAddresses",
                columns: new[] { "ClientIdentifier", "ProtocolVersion" },
                unique: true
            );

            // Convert the device table's own CreatedAtUtc to UTC ticks via a new column rather
            // than an in-place AlterColumn: PostgreSQL has no implicit timestamptz-to-bigint cast, so a plain AlterColumn fails outright against a real server (see ConvertDnssecKeyCreatedAtUtcToTicks).
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUtcTicks",
                table: "NinlilDnsClients",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE "NinlilDnsClients" SET "CreatedAtUtcTicks" = (EXTRACT(EPOCH FROM "CreatedAtUtc") * 1000000)::bigint * 10 + 621355968000000000;
                """
            );

            // Move every existing device's single identifier into its own address row. Before
            // this migration a device carried exactly one identifier, so the device's own Id is
            // reused as its one address row's Id -- unique by construction, and no
            // provider-specific GUID generation is needed.
            migrationBuilder.Sql(
                """
                INSERT INTO "NinlilDnsClientAddresses" ("Id", "ClientId", "ClientIdentifier", "ProtocolVersion", "Address", "FirstSeenUtc", "LastSeenUtc", "CreatedAtUtc", "UpdatedAtUtc", "OriginatingNodeId")
                SELECT "Id", "Id", "ClientIdentifier", "ProtocolVersion", NULL, "CreatedAtUtcTicks", NULL, "CreatedAtUtcTicks", NULL, "OriginatingNodeId"
                FROM "NinlilDnsClients";
                """
            );

            migrationBuilder.DropIndex(
                name: "IX_NinlilDnsClients_ClientIdentifier_ProtocolVersion",
                table: "NinlilDnsClients"
            );

            migrationBuilder.DropColumn(name: "ClientIdentifier", table: "NinlilDnsClients");

            migrationBuilder.DropColumn(name: "ProtocolVersion", table: "NinlilDnsClients");

            migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "NinlilDnsClients");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtcTicks",
                table: "NinlilDnsClients",
                newName: "CreatedAtUtc"
            );

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUtc",
                table: "NinlilDnsClients",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsClientAddresses");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "NinlilDnsClients",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint"
            );

            migrationBuilder.AddColumn<string>(
                name: "ClientIdentifier",
                table: "NinlilDnsClients",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "ProtocolVersion",
                table: "NinlilDnsClients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsClients_ClientIdentifier_ProtocolVersion",
                table: "NinlilDnsClients",
                columns: new[] { "ClientIdentifier", "ProtocolVersion" },
                unique: true
            );
        }
    }
}

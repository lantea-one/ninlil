using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

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
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                Address = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                FirstSeenUtc = table.Column<long>(type: "INTEGER", nullable: false),
                LastSeenUtc = table.Column<long>(type: "INTEGER", nullable: true),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                OriginatingNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
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

        // Move every existing device's single identifier into its own address row. Before
        // this migration a device carried exactly one identifier, so the device's own Id is
        // reused as its one address row's Id -- unique by construction. The device's own
        // CreatedAtUtc is still stored as ISO-8601 text at this point, converted here to UTC
        // ticks (millisecond precision, via julianday) for the new table's tick-typed columns.
        migrationBuilder.Sql(
            """
            INSERT INTO "NinlilDnsClientAddresses" ("Id", "ClientId", "ClientIdentifier", "ProtocolVersion", "Address", "FirstSeenUtc", "LastSeenUtc", "CreatedAtUtc", "UpdatedAtUtc", "OriginatingNodeId")
            SELECT "Id", "Id", "ClientIdentifier", "ProtocolVersion", NULL, CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000, NULL, CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000, NULL, "OriginatingNodeId"
            FROM "NinlilDnsClients";
            """
        );

        // Convert the device table's own CreatedAtUtc from ISO-8601 text to UTC ticks in
        // place, while it is still a TEXT column and before any operation below schedules a
        // table rebuild: raw SQL must never run while a rebuild is pending, since EF defers the
        // rebuild past it. The ticks land as numeric text, which the rebuild's INTEGER column
        // affinity then stores as a true integer.
        migrationBuilder.Sql(
            """
            UPDATE "NinlilDnsClients" SET "CreatedAtUtc" = CAST(ROUND((julianday("CreatedAtUtc") - 2440587.5) * 86400000.0) AS INTEGER) * 10000 + 621355968000000000;
            """
        );

        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsClients_ClientIdentifier_ProtocolVersion",
            table: "NinlilDnsClients"
        );

        migrationBuilder.DropColumn(name: "ClientIdentifier", table: "NinlilDnsClients");

        migrationBuilder.DropColumn(name: "ProtocolVersion", table: "NinlilDnsClients");

        migrationBuilder.AlterColumn<long>(
            name: "CreatedAtUtc",
            table: "NinlilDnsClients",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "TEXT"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsClientAddresses");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            table: "NinlilDnsClients",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER"
        );

        migrationBuilder.AddColumn<string>(
            name: "ClientIdentifier",
            table: "NinlilDnsClients",
            type: "TEXT",
            maxLength: 256,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<string>(
            name: "ProtocolVersion",
            table: "NinlilDnsClients",
            type: "TEXT",
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

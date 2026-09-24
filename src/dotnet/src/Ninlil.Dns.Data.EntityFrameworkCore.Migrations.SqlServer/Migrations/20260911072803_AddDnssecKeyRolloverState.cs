using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDnssecKeyRolloverState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added, and backfilled from IsRetired's own real value, *before* IsRetired is
            // dropped below -- the scaffolded drop-then-add order would have discarded every
            // existing key's own retirement status before this column even existed to receive it.
            migrationBuilder.AddColumn<int>(
                name: "LifecycleState",
                table: "NinlilDnsDnssecKeys",
                type: "int",
                nullable: false,
                defaultValue: 1
            );

            // An already-retired key (IsRetired true) becomes Removed (4); an ordinary key
            // (IsRetired false) becomes Active (1) -- DnssecKeyLifecycleStateEnum's own real
            // numeric values, not the old column's own true/false.
            migrationBuilder.Sql(
                "UPDATE [NinlilDnsDnssecKeys] SET [LifecycleState] = CASE WHEN [IsRetired] = 1 THEN 4 ELSE 1 END;"
            );

            migrationBuilder.DropColumn(name: "IsRetired", table: "NinlilDnsDnssecKeys");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ParentDsConfirmedAtUtc",
                table: "NinlilDnsDnssecKeys",
                type: "datetimeoffset",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "NinlilDnsDnssecKeys",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(
                    new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                    new TimeSpan(0, 0, 0, 0, 0)
                )
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAtUtc",
                table: "NinlilDnsDnssecKeys",
                type: "datetimeoffset",
                nullable: true
            );

            // PublishedAtUtc has no real prior value to migrate at all -- every existing key
            // backfills to its own CreatedAtUtc, matching exactly what a key generated the
            // ordinary way today already gets (see DnssecKeyEntity.PublishedAtUtc's own doc
            // comment).
            migrationBuilder.Sql(
                "UPDATE [NinlilDnsDnssecKeys] SET [PublishedAtUtc] = [CreatedAtUtc];"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LifecycleState", table: "NinlilDnsDnssecKeys");

            migrationBuilder.DropColumn(
                name: "ParentDsConfirmedAtUtc",
                table: "NinlilDnsDnssecKeys"
            );

            migrationBuilder.DropColumn(name: "PublishedAtUtc", table: "NinlilDnsDnssecKeys");

            migrationBuilder.DropColumn(name: "RetiredAtUtc", table: "NinlilDnsDnssecKeys");

            migrationBuilder.AddColumn<bool>(
                name: "IsRetired",
                table: "NinlilDnsDnssecKeys",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
        }
    }
}

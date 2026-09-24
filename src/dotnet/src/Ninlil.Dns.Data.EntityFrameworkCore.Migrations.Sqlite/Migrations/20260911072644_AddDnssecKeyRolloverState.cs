using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDnssecKeyRolloverState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsRetired",
                table: "NinlilDnsDnssecKeys",
                newName: "LifecycleState"
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ParentDsConfirmedAtUtc",
                table: "NinlilDnsDnssecKeys",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "NinlilDnsDnssecKeys",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(
                    new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                    new TimeSpan(0, 0, 0, 0, 0)
                )
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAtUtc",
                table: "NinlilDnsDnssecKeys",
                type: "TEXT",
                nullable: true
            );

            // The rename above reused the old column's own real stored values (SQLite has no
            // native boolean type -- IsRetired was stored as the plain integers 0/1) as this new
            // int-backed enum column's own values, which mean something different:
            // DnssecKeyLifecycleStateEnum.Active is 1, not the old "false," and Removed is 4, not
            // the old "true." Fixed up explicitly, in an order that can never let the two updates
            // collide: every already-retired key (still holding the old value 1) moves to Removed
            // (4) first, then every remaining ordinary key (still holding the old value 0) moves
            // to Active (1).
            migrationBuilder.Sql(
                "UPDATE NinlilDnsDnssecKeys SET LifecycleState = 4 WHERE LifecycleState = 1;"
            );
            migrationBuilder.Sql(
                "UPDATE NinlilDnsDnssecKeys SET LifecycleState = 1 WHERE LifecycleState = 0;"
            );

            // PublishedAtUtc has no real prior value to migrate at all -- every existing key
            // backfills to its own CreatedAtUtc, matching exactly what a key generated the
            // ordinary way today already gets (see DnssecKeyEntity.PublishedAtUtc's own doc
            // comment).
            migrationBuilder.Sql("UPDATE NinlilDnsDnssecKeys SET PublishedAtUtc = CreatedAtUtc;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentDsConfirmedAtUtc",
                table: "NinlilDnsDnssecKeys"
            );

            migrationBuilder.DropColumn(name: "PublishedAtUtc", table: "NinlilDnsDnssecKeys");

            migrationBuilder.DropColumn(name: "RetiredAtUtc", table: "NinlilDnsDnssecKeys");

            // Deliberately a plain structural reversal, with no attempt to losslessly restore the
            // original IsRetired bool from whichever of the five real lifecycle states a key
            // happens to be in at rollback time (a rollback mid-rollover has no single correct
            // true/false answer to reconstruct in the first place) -- consistent with every other
            // migration in this project, none of which attempt a fully data-preserving Down either.
            migrationBuilder.RenameColumn(
                name: "LifecycleState",
                table: "NinlilDnsDnssecKeys",
                newName: "IsRetired"
            );
        }
    }
}

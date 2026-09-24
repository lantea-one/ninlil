using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ConvertDnssecKeyCreatedAtUtcToTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL has no built-in implicit cast from "timestamp with time zone" to
            // "bigint" -- the plain AlterColumn call EF Core's own model diff would otherwise
            // generate here fails at migrate time with "column ... cannot be cast automatically
            // to type bigint" (never caught by a build or by `migrations add` itself, only by
            // actually running this migration against a real PostgreSQL database). A real
            // "USING" expression is required to tell PostgreSQL how to derive the new bigint
            // value from each existing row's own timestamptz value: microseconds since the Unix
            // epoch (the finest-grained precision a PostgreSQL timestamptz actually stores),
            // times 10 to reach .NET's own 100ns tick unit, plus 621355968000000000 -- the fixed
            // number of ticks between 0001-01-01 (DateTime/DateTimeOffset's own epoch) and
            // 1970-01-01 (the Unix epoch), the same constant .NET's own DateTimeOffset.UtcTicks
            // conversion the entity configuration's HasConversion call already applies in
            // managed code. Multiplying by 1000000 while still inside EXTRACT(EPOCH ...)'s own
            // double-precision arithmetic, then rounding via the ::bigint cast, keeps every
            // intermediate value within double precision's exact-integer range (up to 2^53) for
            // any realistic timestamp -- doing the same multiplication by 10,000,000 in one step
            // would not.
            migrationBuilder.Sql(
                """
                ALTER TABLE "NinlilDnsDnssecKeys"
                ALTER COLUMN "CreatedAtUtc" TYPE bigint
                USING ((EXTRACT(EPOCH FROM "CreatedAtUtc") * 1000000)::bigint * 10 + 621355968000000000);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reverse conversion has the identical problem in the opposite direction --
            // PostgreSQL cannot implicitly cast "bigint" back to "timestamp with time zone"
            // either, so this also needs an explicit "USING" expression: undo the tick-to-
            // Unix-epoch-microseconds conversion Up() performs above, then hand the result to
            // to_timestamp(double precision), which returns a real timestamptz. This necessarily
            // loses each value's own sub-microsecond precision (a bigint tick count has 100ns
            // resolution; a PostgreSQL timestamptz only ever had 1-microsecond resolution to
            // begin with) -- harmless, since the value being rolled back here was itself
            // originally derived from a timestamptz of no finer precision the last time this
            // migration's own Up() ran.
            migrationBuilder.Sql(
                """
                ALTER TABLE "NinlilDnsDnssecKeys"
                ALTER COLUMN "CreatedAtUtc" TYPE timestamp with time zone
                USING (to_timestamp((("CreatedAtUtc" - 621355968000000000) / 10)::double precision / 1000000.0));
                """
            );
        }
    }
}

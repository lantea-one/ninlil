using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations;

/// <inheritdoc />
public partial class ConvertDhcpLeaseTimesToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A plain AlterColumn fails on PostgreSQL (no implicit timestamptz-to-bigint cast),
        // so the USING expression converts to UTC ticks (microsecond precision).
        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDhcpLeases"
            ALTER COLUMN "LeaseObtainedAtUtc" TYPE bigint
            USING ((EXTRACT(EPOCH FROM "LeaseObtainedAtUtc") * 1000000)::bigint * 10 + 621355968000000000);
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDhcpLeases"
            ALTER COLUMN "LeaseExpiresAtUtc" TYPE bigint
            USING ((EXTRACT(EPOCH FROM "LeaseExpiresAtUtc") * 1000000)::bigint * 10 + 621355968000000000);
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDhcpLeases"
            ALTER COLUMN "LeaseObtainedAtUtc" TYPE timestamp with time zone
            USING (to_timestamp((("LeaseObtainedAtUtc" - 621355968000000000) / 10)::double precision / 1000000.0));
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "NinlilDhcpLeases"
            ALTER COLUMN "LeaseExpiresAtUtc" TYPE timestamp with time zone
            USING (to_timestamp((("LeaseExpiresAtUtc" - 621355968000000000) / 10)::double precision / 1000000.0));
            """
        );
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class ConvertDhcpLeaseTimesToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQL Server refuses to convert datetimeoffset to bigint, so the ticks go into a
        // new column that replaces the old one.
        migrationBuilder.AddColumn<long>(
            name: "LeaseObtainedAtUtcTicks",
            table: "NinlilDhcpLeases",
            type: "bigint",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDhcpLeases] SET [LeaseObtainedAtUtcTicks] = DATEDIFF_BIG(MICROSECOND, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset), [LeaseObtainedAtUtc]) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "LeaseObtainedAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseObtainedAtUtcTicks",
            table: "NinlilDhcpLeases",
            newName: "LeaseObtainedAtUtc"
        );

        migrationBuilder.AddColumn<long>(
            name: "LeaseExpiresAtUtcTicks",
            table: "NinlilDhcpLeases",
            type: "bigint",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDhcpLeases] SET [LeaseExpiresAtUtcTicks] = DATEDIFF_BIG(MICROSECOND, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset), [LeaseExpiresAtUtc]) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "LeaseExpiresAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseExpiresAtUtcTicks",
            table: "NinlilDhcpLeases",
            newName: "LeaseExpiresAtUtc"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Ticks back to datetimeoffset. DATEADD takes an int, so the offset is applied as
        // days, seconds and microseconds.
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LeaseObtainedAtUtcValue",
            table: "NinlilDhcpLeases",
            type: "datetimeoffset",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDhcpLeases] SET [LeaseObtainedAtUtcValue] = DATEADD(MICROSECOND, (([LeaseObtainedAtUtc] - 621355968000000000) / 10) % 1000000, DATEADD(SECOND, (([LeaseObtainedAtUtc] - 621355968000000000) / 10000000) % 86400, DATEADD(DAY, ([LeaseObtainedAtUtc] - 621355968000000000) / 864000000000, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset))));
            """
        );

        migrationBuilder.DropColumn(name: "LeaseObtainedAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseObtainedAtUtcValue",
            table: "NinlilDhcpLeases",
            newName: "LeaseObtainedAtUtc"
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LeaseExpiresAtUtcValue",
            table: "NinlilDhcpLeases",
            type: "datetimeoffset",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDhcpLeases] SET [LeaseExpiresAtUtcValue] = DATEADD(MICROSECOND, (([LeaseExpiresAtUtc] - 621355968000000000) / 10) % 1000000, DATEADD(SECOND, (([LeaseExpiresAtUtc] - 621355968000000000) / 10000000) % 86400, DATEADD(DAY, ([LeaseExpiresAtUtc] - 621355968000000000) / 864000000000, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset))));
            """
        );

        migrationBuilder.DropColumn(name: "LeaseExpiresAtUtc", table: "NinlilDhcpLeases");

        migrationBuilder.RenameColumn(
            name: "LeaseExpiresAtUtcValue",
            table: "NinlilDhcpLeases",
            newName: "LeaseExpiresAtUtc"
        );
    }
}

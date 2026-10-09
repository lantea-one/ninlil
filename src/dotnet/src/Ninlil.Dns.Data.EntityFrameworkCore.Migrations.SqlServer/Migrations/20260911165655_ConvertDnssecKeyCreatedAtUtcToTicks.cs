using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class ConvertDnssecKeyCreatedAtUtcToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQL Server refuses to convert datetimeoffset to bigint at all -- a plain AlterColumn
        // fails with "Operand type clash" even against an empty table -- so the ticks value is
        // written into a new column, the old one dropped, and the new one renamed into place.
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtUtcTicks",
            table: "NinlilDnsDnssecKeys",
            type: "bigint",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE [NinlilDnsDnssecKeys] SET [CreatedAtUtcTicks] = DATEDIFF_BIG(MICROSECOND, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset), [CreatedAtUtc]) * 10 + 621355968000000000;
            """
        );

        migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "NinlilDnsDnssecKeys");

        migrationBuilder.RenameColumn(
            name: "CreatedAtUtcTicks",
            table: "NinlilDnsDnssecKeys",
            newName: "CreatedAtUtc"
        );

        migrationBuilder.AlterColumn<long>(
            name: "CreatedAtUtc",
            table: "NinlilDnsDnssecKeys",
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
        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            table: "NinlilDnsDnssecKeys",
            type: "datetimeoffset",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint"
        );
    }
}

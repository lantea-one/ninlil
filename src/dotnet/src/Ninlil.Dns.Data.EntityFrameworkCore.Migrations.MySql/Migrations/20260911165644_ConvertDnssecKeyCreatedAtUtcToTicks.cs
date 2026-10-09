using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class ConvertDnssecKeyCreatedAtUtcToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<long>(
            name: "CreatedAtUtc",
            table: "NinlilDnsDnssecKeys",
            type: "bigint",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "datetime(6)"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            table: "NinlilDnsDnssecKeys",
            type: "datetime(6)",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint"
        );
    }
}

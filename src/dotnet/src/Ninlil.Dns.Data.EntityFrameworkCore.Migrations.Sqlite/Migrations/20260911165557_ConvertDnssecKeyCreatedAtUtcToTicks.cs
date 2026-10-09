using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class ConvertDnssecKeyCreatedAtUtcToTicks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<long>(
            name: "CreatedAtUtc",
            table: "NinlilDnsDnssecKeys",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "TEXT"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            table: "NinlilDnsDnssecKeys",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER"
        );
    }
}

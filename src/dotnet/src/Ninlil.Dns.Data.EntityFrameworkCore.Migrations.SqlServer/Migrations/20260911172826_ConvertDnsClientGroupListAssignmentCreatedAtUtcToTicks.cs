using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ConvertDnsClientGroupListAssignmentCreatedAtUtcToTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server refuses to convert datetimeoffset to bigint at all -- a plain AlterColumn
            // fails with "Operand type clash" even against an empty table -- so the ticks value is
            // written into a new column, the old one dropped, and the new one renamed into place.
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUtcTicks",
                table: "NinlilDnsClientGroupListAssignments",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE [NinlilDnsClientGroupListAssignments] SET [CreatedAtUtcTicks] = DATEDIFF_BIG(MICROSECOND, CAST('1970-01-01T00:00:00+00:00' AS datetimeoffset), [CreatedAtUtc]) * 10 + 621355968000000000;
                """
            );

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "NinlilDnsClientGroupListAssignments"
            );

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtcTicks",
                table: "NinlilDnsClientGroupListAssignments",
                newName: "CreatedAtUtc"
            );

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAtUtc",
                table: "NinlilDnsClientGroupListAssignments",
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
                table: "NinlilDnsClientGroupListAssignments",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint"
            );
        }
    }
}

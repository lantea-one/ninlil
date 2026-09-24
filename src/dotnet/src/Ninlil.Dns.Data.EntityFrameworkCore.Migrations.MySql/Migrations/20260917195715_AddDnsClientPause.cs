using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsClientPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedUntilUtc",
                table: "NinlilDnsClients",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedUntilUtc",
                table: "NinlilDnsClientGroups",
                type: "datetime(6)",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PausedUntilUtc", table: "NinlilDnsClients");

            migrationBuilder.DropColumn(name: "PausedUntilUtc", table: "NinlilDnsClientGroups");
        }
    }
}

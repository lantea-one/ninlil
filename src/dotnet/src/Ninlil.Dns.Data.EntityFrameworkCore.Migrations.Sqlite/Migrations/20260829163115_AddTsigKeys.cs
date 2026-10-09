using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddTsigKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsTsigKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                AlgorithmName = table.Column<string>(
                    type: "TEXT",
                    maxLength: 255,
                    nullable: false
                ),
                SecretEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsTsigKeys", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsTsigKeys_Name",
            table: "NinlilDnsTsigKeys",
            column: "Name",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsTsigKeys");
    }
}

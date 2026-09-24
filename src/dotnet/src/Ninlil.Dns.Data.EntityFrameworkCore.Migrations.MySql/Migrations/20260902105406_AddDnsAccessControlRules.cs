using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDnsAccessControlRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "NinlilDnsAccessControlRules",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(
                            type: "char(36)",
                            nullable: false,
                            collation: "ascii_general_ci"
                        ),
                        Name = table
                            .Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Cidr = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Capabilities = table.Column<int>(type: "int", nullable: false),
                        IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        CreatedAtUtc = table.Column<DateTimeOffset>(
                            type: "datetime(6)",
                            nullable: false
                        ),
                        UpdatedAtUtc = table.Column<DateTimeOffset>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_NinlilDnsAccessControlRules", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsAccessControlRules");
        }
    }
}

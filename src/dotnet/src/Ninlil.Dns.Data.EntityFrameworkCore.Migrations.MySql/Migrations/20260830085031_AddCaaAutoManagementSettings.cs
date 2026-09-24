using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddCaaAutoManagementSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoManageCaaRecords",
                table: "NinlilDnsZones",
                type: "tinyint(1)",
                nullable: true
            );

            migrationBuilder
                .AddColumn<string>(
                    name: "CaaIssuerDomain",
                    table: "NinlilDnsZones",
                    type: "varchar(255)",
                    maxLength: 255,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .AddColumn<string>(
                    name: "ManagedByFeature",
                    table: "NinlilDnsRecords",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AutoManageCaaRecords", table: "NinlilDnsZones");

            migrationBuilder.DropColumn(name: "CaaIssuerDomain", table: "NinlilDnsZones");

            migrationBuilder.DropColumn(name: "ManagedByFeature", table: "NinlilDnsRecords");
        }
    }
}

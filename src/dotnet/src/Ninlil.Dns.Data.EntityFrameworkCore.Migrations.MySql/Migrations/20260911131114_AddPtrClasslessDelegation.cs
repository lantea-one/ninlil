using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddPtrClasslessDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PtrIpv4PrefixLength",
                table: "NinlilDnsZones",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PtrIpv6PrefixLength",
                table: "NinlilDnsZones",
                type: "int",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PtrIpv4PrefixLength", table: "NinlilDnsZones");

            migrationBuilder.DropColumn(name: "PtrIpv6PrefixLength", table: "NinlilDnsZones");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PtrIpv6PrefixLength",
                table: "NinlilDnsZones",
                type: "INTEGER",
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

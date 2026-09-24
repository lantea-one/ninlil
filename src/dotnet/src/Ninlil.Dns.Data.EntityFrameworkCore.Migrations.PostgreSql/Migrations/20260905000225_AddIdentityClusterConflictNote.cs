using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityClusterConflictNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClusterConflictNote",
                table: "NinlilDnsUsers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ClusterConflictNote",
                table: "NinlilDnsRoles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ClusterConflictNote", table: "NinlilDnsUsers");

            migrationBuilder.DropColumn(name: "ClusterConflictNote", table: "NinlilDnsRoles");
        }
    }
}

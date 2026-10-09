using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddUserEntityExternalIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExternalIdentityProvider",
            table: "NinlilDnsUsers",
            type: "TEXT",
            maxLength: 64,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "ExternalSubjectId",
            table: "NinlilDnsUsers",
            type: "TEXT",
            maxLength: 256,
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUsers_ExternalIdentityProvider_ExternalSubjectId",
            table: "NinlilDnsUsers",
            columns: new[] { "ExternalIdentityProvider", "ExternalSubjectId" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NinlilDnsUsers_ExternalIdentityProvider_ExternalSubjectId",
            table: "NinlilDnsUsers"
        );

        migrationBuilder.DropColumn(name: "ExternalIdentityProvider", table: "NinlilDnsUsers");

        migrationBuilder.DropColumn(name: "ExternalSubjectId", table: "NinlilDnsUsers");
    }
}

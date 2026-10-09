using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddTwoFactorRecoveryCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TwoFactorRecoveryCodesEncrypted",
            table: "NinlilDnsUsers",
            type: "nvarchar(max)",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TwoFactorRecoveryCodesEncrypted",
            table: "NinlilDnsUsers"
        );
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudDnsSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CloudProviderCredentialId",
                table: "NinlilDnsZones",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CloudProviderZoneId",
                table: "NinlilDnsZones",
                type: "TEXT",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CloudSyncInterval",
                table: "NinlilDnsZones",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 5, 0, 0)
            );

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromSync",
                table: "NinlilDnsRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsCloudDnsProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ProviderType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    CredentialsEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinlilDnsCloudDnsProviderCredentials", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NinlilDnsCloudDnsProviderCredentials_Name",
                table: "NinlilDnsCloudDnsProviderCredentials",
                column: "Name",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NinlilDnsCloudDnsProviderCredentials");

            migrationBuilder.DropColumn(name: "CloudProviderCredentialId", table: "NinlilDnsZones");

            migrationBuilder.DropColumn(name: "CloudProviderZoneId", table: "NinlilDnsZones");

            migrationBuilder.DropColumn(name: "CloudSyncInterval", table: "NinlilDnsZones");

            migrationBuilder.DropColumn(name: "ExcludeFromSync", table: "NinlilDnsRecords");
        }
    }
}

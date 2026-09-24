using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql.Migrations
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
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CloudProviderZoneId",
                table: "NinlilDnsZones",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true
            );

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CloudSyncInterval",
                table: "NinlilDnsZones",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 5, 0, 0)
            );

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromSync",
                table: "NinlilDnsRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.CreateTable(
                name: "NinlilDnsCloudDnsProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    ProviderType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CredentialsEncrypted = table.Column<string>(type: "text", nullable: false),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    Comments = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
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

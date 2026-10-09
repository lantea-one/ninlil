using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddCloudDnsSync : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CloudProviderCredentialId",
            table: "NinlilDnsZones",
            type: "uniqueidentifier",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "CloudProviderZoneId",
            table: "NinlilDnsZones",
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: true
        );

        migrationBuilder.AddColumn<TimeSpan>(
            name: "CloudSyncInterval",
            table: "NinlilDnsZones",
            type: "time",
            nullable: false,
            defaultValue: new TimeSpan(0, 0, 5, 0, 0)
        );

        migrationBuilder.AddColumn<bool>(
            name: "ExcludeFromSync",
            table: "NinlilDnsRecords",
            type: "bit",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsCloudDnsProviderCredentials",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(
                    type: "nvarchar(255)",
                    maxLength: 255,
                    nullable: false
                ),
                ProviderType = table.Column<string>(
                    type: "nvarchar(32)",
                    maxLength: 32,
                    nullable: false
                ),
                CredentialsEncrypted = table.Column<string>(
                    type: "nvarchar(max)",
                    nullable: false
                ),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                Comments = table.Column<string>(
                    type: "nvarchar(max)",
                    maxLength: 4096,
                    nullable: true
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false
                ),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
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

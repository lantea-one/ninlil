using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql.Migrations;

/// <inheritdoc />
public partial class AddCloudDnsSync : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CloudProviderCredentialId",
            table: "NinlilDnsZones",
            type: "char(36)",
            nullable: true,
            collation: "ascii_general_ci"
        );

        migrationBuilder
            .AddColumn<string>(
                name: "CloudProviderZoneId",
                table: "NinlilDnsZones",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true
            )
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "CloudSyncInterval",
            table: "NinlilDnsZones",
            type: "time(6)",
            nullable: false,
            defaultValue: new TimeSpan(0, 0, 5, 0, 0)
        );

        migrationBuilder.AddColumn<bool>(
            name: "ExcludeFromSync",
            table: "NinlilDnsRecords",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder
            .CreateTable(
                name: "NinlilDnsCloudDnsProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"
                    ),
                    Name = table
                        .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderType = table
                        .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CredentialsEncrypted = table
                        .Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Comments = table
                        .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_NinlilDnsCloudDnsProviderCredentials", x => x.Id);
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4");

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

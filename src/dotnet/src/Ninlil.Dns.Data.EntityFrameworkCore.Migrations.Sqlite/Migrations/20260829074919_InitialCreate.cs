using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NinlilDnsLeases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProtocolVersion = table.Column<string>(
                    type: "TEXT",
                    maxLength: 16,
                    nullable: false
                ),
                ScopeName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ClientIdentifier = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: false
                ),
                IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                HostName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                LeaseObtainedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsLeases", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                Message = table.Column<string>(type: "TEXT", nullable: false),
                Exception = table.Column<string>(type: "TEXT", nullable: true),
                PropertiesJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsLogs", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsMetrics",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Value = table.Column<double>(type: "REAL", nullable: false),
                DimensionsJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsMetrics", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsRoles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                NormalizedName = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: false
                ),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsRoles", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsUsers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                NormalizedUserName = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: false
                ),
                DisplayName = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: true
                ),
                Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(
                    type: "TEXT",
                    maxLength: 256,
                    nullable: true
                ),
                EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                TwoFactorTotpSecretEncrypted = table.Column<string>(
                    type: "TEXT",
                    nullable: true
                ),
                LockoutEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsUsers", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsZones",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                IsDnssecSigned = table.Column<bool>(type: "INTEGER", nullable: false),
                DnssecAlgorithm = table.Column<string>(
                    type: "TEXT",
                    maxLength: 32,
                    nullable: true
                ),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                PrimaryServerAddresses = table.Column<string>(type: "TEXT", nullable: true),
                ForwarderAddresses = table.Column<string>(type: "TEXT", nullable: true),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsZones", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsRoleClaims",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                ClaimType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ClaimValue = table.Column<string>(
                    type: "TEXT",
                    maxLength: 1024,
                    nullable: false
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsRoleClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsRoleClaims_NinlilDnsRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "NinlilDnsRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsApiTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                TokenHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsApiTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsApiTokens_NinlilDnsUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "NinlilDnsUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                RefreshTokenHash = table.Column<string>(
                    type: "TEXT",
                    maxLength: 512,
                    nullable: false
                ),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                UserAgent = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsSessions_NinlilDnsUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "NinlilDnsUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsUserClaims",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                ClaimType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ClaimValue = table.Column<string>(
                    type: "TEXT",
                    maxLength: 1024,
                    nullable: false
                ),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsUserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsUserClaims_NinlilDnsUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "NinlilDnsUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsUserRoles",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsUserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_NinlilDnsUserRoles_NinlilDnsRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "NinlilDnsRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
                table.ForeignKey(
                    name: "FK_NinlilDnsUserRoles_NinlilDnsUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "NinlilDnsUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "NinlilDnsRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Class = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                TimeToLiveSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                DataJson = table.Column<string>(type: "TEXT", nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Comments = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NinlilDnsRecords", x => x.Id);
                table.ForeignKey(
                    name: "FK_NinlilDnsRecords_NinlilDnsZones_ZoneId",
                    column: x => x.ZoneId,
                    principalTable: "NinlilDnsZones",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsApiTokens_TokenHash",
            table: "NinlilDnsApiTokens",
            column: "TokenHash",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsApiTokens_UserId",
            table: "NinlilDnsApiTokens",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsLeases_ScopeName_ClientIdentifier",
            table: "NinlilDnsLeases",
            columns: new[] { "ScopeName", "ClientIdentifier" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsLeases_ScopeName_IpAddress",
            table: "NinlilDnsLeases",
            columns: new[] { "ScopeName", "IpAddress" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsLogs_TimestampUtc_Severity",
            table: "NinlilDnsLogs",
            columns: new[] { "TimestampUtc", "Severity" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsMetrics_Category_Name_TimestampUtc",
            table: "NinlilDnsMetrics",
            columns: new[] { "Category", "Name", "TimestampUtc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRecords_ZoneId_Name_Type",
            table: "NinlilDnsRecords",
            columns: new[] { "ZoneId", "Name", "Type" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRoleClaims_RoleId",
            table: "NinlilDnsRoleClaims",
            column: "RoleId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsRoles_NormalizedName",
            table: "NinlilDnsRoles",
            column: "NormalizedName",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsSessions_RefreshTokenHash",
            table: "NinlilDnsSessions",
            column: "RefreshTokenHash",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsSessions_UserId",
            table: "NinlilDnsSessions",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUserClaims_UserId",
            table: "NinlilDnsUserClaims",
            column: "UserId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUserRoles_RoleId",
            table: "NinlilDnsUserRoles",
            column: "RoleId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUsers_NormalizedEmail",
            table: "NinlilDnsUsers",
            column: "NormalizedEmail"
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsUsers_NormalizedUserName",
            table: "NinlilDnsUsers",
            column: "NormalizedUserName",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_NinlilDnsZones_Name",
            table: "NinlilDnsZones",
            column: "Name",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NinlilDnsApiTokens");

        migrationBuilder.DropTable(name: "NinlilDnsLeases");

        migrationBuilder.DropTable(name: "NinlilDnsLogs");

        migrationBuilder.DropTable(name: "NinlilDnsMetrics");

        migrationBuilder.DropTable(name: "NinlilDnsRecords");

        migrationBuilder.DropTable(name: "NinlilDnsRoleClaims");

        migrationBuilder.DropTable(name: "NinlilDnsSessions");

        migrationBuilder.DropTable(name: "NinlilDnsUserClaims");

        migrationBuilder.DropTable(name: "NinlilDnsUserRoles");

        migrationBuilder.DropTable(name: "NinlilDnsZones");

        migrationBuilder.DropTable(name: "NinlilDnsRoles");

        migrationBuilder.DropTable(name: "NinlilDnsUsers");
    }
}
